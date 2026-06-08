# Contributing to SharpMinerals

Thanks for your interest / TODO

## Ground rules

- Be kind - this is a small, early community.
- Open an issue or ask in an existing one first before creating a PR, so we can discuss how can it be resolved and
  make sure we won't waste our time.
- If you use an LLM - read and verify output manually. Make sure you understand what it does, as 
  most of the time you'd need to introduce changes to code, especially AI-assisted one.

## Building & testing

Build the solution, run tests and test your PR using a real client:
```sh
dotnet build SharpMinerals.sln
dotnet test
dotnet run --project SharpMinerals.CLI
```

A pull request should build cleanly and keep the tests green; new behavior should
come with a test where practical.

Also make sure to test AOT build:
```sh
dotnet publish -p:AOT=true
```

## License

By contributing, you agree that your contributions are licensed under the
project's [Apache License 2.0](LICENSE).
