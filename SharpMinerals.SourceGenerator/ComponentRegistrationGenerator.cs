using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpMinerals.SourceGenerator;

/// <summary>
/// Incremental generator that emits a <c>[ModuleInitializer]</c> registering every <c>[Component]</c> type in the
/// compilation with <c>SharpMinerals.Components.ComponentRegistry</c> - our replacement for Arch's
/// <c>Arch.AOT.SourceGenerator</c> (which only registered Arch component arrays). The component's namespace is
/// read from the assembly's <c>[ModInfo("&lt;id&gt;", ...)]</c> (the mod id), so each mod's components are namespaced
/// under it without runtime reflection or a hand-written list - the assembly "carries its own components".
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentRegistrationGenerator : IIncrementalGenerator {
    const string ComponentAttribute = "global::SharpMinerals.Components.ComponentAttribute";
    const string ModInfoAttribute = "global::SharpMinerals.Modding.ModInfoAttribute";
    const string PersistentInterface = "global::SharpMinerals.Components.IPersistentComponent";

    // A discovered component: its fully-qualified name and whether it persists (implements IPersistentComponent,
    // so it needs a read-factory wired). A value tuple - equatable for the incremental cache, no netstandard polyfill.

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Every [Component] type -> (name, persistent?).
        var components = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetComponent(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!.Value)
            .Collect();

        // The mod namespace from [ModInfo("<id>", ...)] (there is one per mod assembly).
        var modNamespace = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetModNamespace(ctx))
            .Where(static ns => ns is not null)
            .Collect();

        context.RegisterSourceOutput(components.Combine(modNamespace),
            static (spc, pair) => Generate(spc, pair.Left, pair.Right));
    }

    static (string Name, bool Persistent)? GetComponent(GeneratorSyntaxContext ctx) {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not ITypeSymbol symbol || !HasAttribute(symbol, ComponentAttribute))
            return null;
        var persistent = symbol.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == PersistentInterface);
        return (symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), persistent);
    }

    static string? GetModNamespace(GeneratorSyntaxContext ctx) {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not ITypeSymbol symbol) return null;
        foreach (var attr in symbol.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ModInfoAttribute
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string id)
                return id;
        return null;
    }

    static bool HasAttribute(ISymbol symbol, string fullyQualifiedName) {
        foreach (var attr in symbol.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName)
                return true;
        return false;
    }

    static void Generate(SourceProductionContext spc, ImmutableArray<(string Name, bool Persistent)> components, ImmutableArray<string?> modNamespaces) {
        if (components.IsDefaultOrEmpty) return;
        // No [ModInfo] -> nothing to namespace under (the assembly isn't a mod); skip rather than guess.
        var ns = modNamespaces.FirstOrDefault(n => n is not null);
        if (ns is null) return;

        var calls = new StringBuilder();
        foreach (var c in components.Distinct())
            // A persistent component also wires its read-factory (the type's `static Read`); others register identity only.
            calls.AppendLine(c.Persistent
                ? $"global::SharpMinerals.Components.ComponentRegistry.Register<{c.Name}>(\"{ns}\", static s => {c.Name}.Read(s));"
                : $"global::SharpMinerals.Components.ComponentRegistry.Register<{c.Name}>(\"{ns}\");");

        var source = $$"""
            using System.Runtime.CompilerServices;

            namespace SharpMinerals.Components.Generated
            {
                /// <summary>Registers this assembly's [Component] types with the ComponentRegistry at module load.</summary>
                internal static class GeneratedComponentRegistration
                {
                    [ModuleInitializer]
                    internal static void Initialize()
                    {
                        {{calls}}
                    }
                }
            }
            """;

        var formatted = CSharpSyntaxTree.ParseText(source).GetRoot().NormalizeWhitespace().ToFullString();
        spc.AddSource("GeneratedComponentRegistration.g.cs", SourceText.From("// <auto-generated/>\n" + formatted, Encoding.UTF8));
    }
}
