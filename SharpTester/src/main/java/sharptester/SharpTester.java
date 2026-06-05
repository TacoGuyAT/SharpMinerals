package sharptester;

import baritone.api.BaritoneAPI;
import baritone.api.pathing.goals.GoalXZ;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.networking.v1.PacketByteBufs;
import net.fabricmc.fabric.api.networking.v1.PacketSender;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.ChatScreen;
import net.minecraft.network.PacketByteBuf;
import net.minecraft.util.Hand;
import net.minecraft.util.Identifier;

/**
 * A generic, server-driven test harness. The server pushes commands over the
 * {@code sharptester:cmd} Custom Payload channel and this mod executes them, so test
 * scenarios change with no mod rebuild or client restart. Results are reported back
 * on the same channel. Any client network exception is captured by
 * {@link sharptester.mixin.ClientConnectionMixin}, and the client exits on disconnect
 * so automated runs never block each other.
 *
 * Commands: {@code break <x> <y> <z>}, {@code mine <block> [count]}, {@code goto <x> <z>},
 * {@code place|use <x> <y> <z> [face]} (right-click a block: places the held block, or opens a container),
 * {@code attack <id>}, {@code look <yaw> <pitch>}, {@code drop [all]}, {@code select <slot>},
 * {@code count <type>}, {@code say <text>}, {@code cmd <text>}, {@code stop}, {@code exit},
 * {@code tree [name]} (the command tree the client received), {@code suggest <text>} (tab-completions,
 * including the server's ask_server round-trip), {@code pos}, {@code held}, {@code health},
 * {@code chatlast} (the last chat/system message received), {@code click <syncId> <slot> <button> <mode>}
 * (a real inventory click — the client builds the actual Click Container packet), {@code slot <i>} and
 * {@code cursor} (inspect the open screen handler's slot/cursor — for verifying click outcomes),
 * {@code set <slot> <item> [count]} (creative set-slot for test setup; slot -1 throws into the world).
 */
public class SharpTester implements ClientModInitializer {
    public static final Identifier CHANNEL = new Identifier("sharptester", "cmd");

    /** The text of the most recent chat/system message the client received (see ClientPlayNetworkHandlerMixin),
     *  so a test can assert the server's chat components actually rendered. */
    public static volatile String lastChat = "";

    @Override
    public void onInitializeClient() {
        // Close the game when the connection drops so a crashed/kicked run frees the
        // instance for the next launch.
        ClientPlayConnectionEvents.DISCONNECT.register((handler, client) -> {
            System.err.println("[SharpTester] disconnected — stopping client");
            client.scheduleStop();
        });

        // On join: turn on the F3 debug overlay, and open the chat screen so the window keeps a
        // screen open and never grabs the mouse cursor — done on the next client tick, right
        // before the in-game screen would otherwise close and hook mouse input.
        ClientPlayConnectionEvents.JOIN.register((handler, sender, client) -> client.execute(() -> {
            client.options.debugEnabled = true;
            client.setScreen(new ChatScreen(""));
        }));

        ClientPlayNetworking.registerGlobalReceiver(CHANNEL, (client, handler, buf, sender) -> {
            String command = buf.readString();
            client.execute(() -> execute(client, command, sender));
        });
    }

    private void execute(MinecraftClient mc, String line, PacketSender reply) {
        String[] a = line.trim().split("\\s+");
        if (a.length == 0 || a[0].isEmpty()) return;
        System.err.println("[SharpTester] command: " + line);
        String rest = line.trim().contains(" ") ? line.trim().substring(line.trim().indexOf(' ') + 1) : "";

        String result;
        try {
            switch (a[0].toLowerCase()) {
                case "break": {
                    // Let Baritone path to and mine out the specific block (a 1-block
                    // selection cleared with "cleararea") — works out of reach.
                    int x = Integer.parseInt(a[1]), y = Integer.parseInt(a[2]), z = Integer.parseInt(a[3]);
                    var pos = new baritone.api.utils.BetterBlockPos(x, y, z);
                    var baritone = BaritoneAPI.getProvider().getPrimaryBaritone();
                    baritone.getSelectionManager().removeAllSelections();
                    baritone.getSelectionManager().addSelection(pos, pos);
                    baritone.getCommandManager().execute("sel cleararea");
                    result = "breaking (" + x + " " + y + " " + z + ") via baritone";
                    break;
                }
                case "mine": {
                    int count = a.length > 2 ? Integer.parseInt(a[2]) : 8;
                    BaritoneAPI.getSettings().allowSprint.value = true;
                    BaritoneAPI.getProvider().getPrimaryBaritone().getMineProcess().mineByName(count, a[1]);
                    result = "mining " + a[1] + " x" + count;
                    break;
                }
                case "goto": {
                    int x = Integer.parseInt(a[1]), z = Integer.parseInt(a[2]);
                    BaritoneAPI.getProvider().getPrimaryBaritone().getCustomGoalProcess().setGoalAndPath(new GoalXZ(x, z));
                    result = "goto " + x + " " + z;
                    break;
                }
                case "attack": {
                    // Attack a specific entity by network id. Succeeding at all proves
                    // that entity is rendered client-side (the visibility signal). Kept
                    // general (id is a parameter) so scenarios compose without changing the mod.
                    int id = Integer.parseInt(a[1]);
                    var target = mc.world.getEntityById(id);
                    if (target == null) { result = "no entity " + id; break; }
                    mc.interactionManager.attackEntity(mc.player, target);
                    mc.player.swingHand(Hand.MAIN_HAND);
                    result = "attacked entity " + id + " (" + target.getName().getString() + ")";
                    break;
                }
                case "look": {
                    mc.player.setYaw(Float.parseFloat(a[1]));
                    mc.player.setPitch(Float.parseFloat(a[2]));
                    result = "look " + a[1] + " " + a[2];
                    break;
                }
                case "stop": {
                    BaritoneAPI.getProvider().getPrimaryBaritone().getPathingBehavior().cancelEverything();
                    result = "stopped";
                    break;
                }
                case "say": {
                    mc.getNetworkHandler().sendChatMessage(rest);
                    result = "said: " + rest;
                    break;
                }
                case "cmd": {
                    mc.getNetworkHandler().sendChatCommand(rest);
                    result = "ran /" + rest;
                    break;
                }
                case "drop": {
                    // Drop the held item (Q / Ctrl+Q) — exercises the server's item-toss path. Sends the
                    // Player Action drop, so the server spawns the item entity and announces it back.
                    boolean whole = a.length > 1 && a[1].equalsIgnoreCase("all");
                    mc.player.dropSelectedItem(whole);
                    result = "dropped " + (whole ? "stack" : "one");
                    break;
                }
                case "select": {
                    // Change the selected hotbar slot (sends Set Held Item) — exercises equipment sync.
                    int slot = Integer.parseInt(a[1]) & 8;
                    mc.player.getInventory().selectedSlot = slot;
                    mc.getNetworkHandler().sendPacket(new net.minecraft.network.packet.c2s.play.UpdateSelectedSlotC2SPacket(slot));
                    result = "selected hotbar " + slot;
                    break;
                }
                case "count": {
                    // Count the client-rendered entities whose type id contains the argument (e.g. "count
                    // item"). The verification primitive: proves the server spawned + announced an entity the
                    // client actually sees, round-trip. Reported back over the channel for the harness to read.
                    String needle = rest.toLowerCase();
                    int n = 0;
                    for (var e : mc.world.getEntities())
                        if (net.minecraft.registry.Registries.ENTITY_TYPE.getId(e.getType()).toString().contains(needle)) n++;
                    result = "count " + needle + " = " + n;
                    break;
                }
                case "tree": {
                    // Report the command tree the client built from the server's Declare Commands packet:
                    // the root's command names, or a named command's direct children. Proves 0x10 arrived
                    // and parsed.
                    var dispatcher = mc.getNetworkHandler().getCommandDispatcher();
                    var node = a.length > 1 ? dispatcher.getRoot().getChild(a[1]) : dispatcher.getRoot();
                    if (node == null) { result = "tree: no command " + a[1]; break; }
                    StringBuilder sb = new StringBuilder("tree " + (a.length > 1 ? a[1] : "") + "=");
                    for (var child : node.getChildren()) sb.append(' ').append(child.getName());
                    result = sb.toString();
                    break;
                }
                case "suggest": {
                    // Ask the client's command dispatcher for completions of <text> (no leading '/'). For
                    // server-suggested (ask_server) arguments this drives the real 0x09→0x0F round-trip, so the
                    // matches reported back are what the SERVER returned. Replied asynchronously when the future
                    // resolves, so we return early instead of sending the synchronous result below.
                    var nh = mc.getNetworkHandler();
                    var parse = nh.getCommandDispatcher().parse(rest, nh.getCommandSource());
                    nh.getCommandDispatcher().getCompletionSuggestions(parse).thenAccept(suggestions -> {
                        var rng = suggestions.getRange();
                        // Report the replacement range too: a wrong range is what makes matches NOT display in
                        // the chat UI even though the list is non-empty.
                        StringBuilder sb = new StringBuilder("suggest [" + rest + "] range=" + rng.getStart() + "+" + rng.getLength() + " =");
                        for (var s : suggestions.getList()) sb.append(' ').append(s.getText());
                        mc.execute(() -> {
                            PacketByteBuf done = PacketByteBufs.create();
                            done.writeString(sb.toString());
                            ClientPlayNetworking.send(CHANNEL, done);
                        });
                    });
                    return;
                }
                case "pos": {
                    var p = mc.player;
                    result = String.format(java.util.Locale.ROOT, "pos %.2f %.2f %.2f yaw %.1f pitch %.1f",
                        p.getX(), p.getY(), p.getZ(), p.getYaw(), p.getPitch());
                    break;
                }
                case "held": {
                    var inv = mc.player.getInventory();
                    var stack = inv.getMainHandStack();
                    var id = net.minecraft.registry.Registries.ITEM.getId(stack.getItem());
                    result = "held slot " + inv.selectedSlot + " = " + id + " x" + stack.getCount();
                    break;
                }
                case "health": {
                    result = String.format(java.util.Locale.ROOT, "health %.1f food %d",
                        mc.player.getHealth(), mc.player.getHungerManager().getFoodLevel());
                    break;
                }
                case "chatlast": {
                    result = "chatlast = " + lastChat;
                    break;
                }
                case "click": {
                    // A real inventory click: the client builds and sends the actual Click Container packet, so
                    // the server interprets the real protocol (mode/button/slot), not a hand-built assumption.
                    // mode 0..6 = PICKUP, QUICK_MOVE, SWAP, CLONE, THROW, QUICK_CRAFT, PICKUP_ALL. Window 0 is
                    // the player's own inventory (always open).
                    int syncId = Integer.parseInt(a[1]), slot = Integer.parseInt(a[2]);
                    int button = Integer.parseInt(a[3]), mode = Integer.parseInt(a[4]);
                    mc.interactionManager.clickSlot(syncId, slot, button,
                        net.minecraft.screen.slot.SlotActionType.values()[mode], mc.player);
                    result = "clicked sync " + syncId + " slot " + slot + " btn " + button + " mode " + mode;
                    break;
                }
                case "slot": {
                    // Report a slot of the currently-open screen handler (window 0 = the player's inventory),
                    // indexed the same way `click` is — so a test can click then assert the resulting contents.
                    int i = Integer.parseInt(a[1]);
                    var stack = mc.player.currentScreenHandler.getSlot(i).getStack();
                    result = "slot " + i + " = "
                        + net.minecraft.registry.Registries.ITEM.getId(stack.getItem()) + " x" + stack.getCount();
                    break;
                }
                case "cursor": {
                    var stack = mc.player.currentScreenHandler.getCursorStack();
                    result = "cursor = "
                        + net.minecraft.registry.Registries.ITEM.getId(stack.getItem()) + " x" + stack.getCount();
                    break;
                }
                case "set": {
                    // Creative set-slot (the client is in creative): place a known stack in a slot to set up
                    // inventory state for a test, OR slot -1 to throw it into the world. Sends the real Set
                    // Creative Mode Slot packet. `set <slot> <item> [count]`, e.g. `set 36 stone 64`.
                    int slot = Integer.parseInt(a[1]);
                    var item = net.minecraft.registry.Registries.ITEM.get(new Identifier(a[2]));
                    int count = a.length > 3 ? Integer.parseInt(a[3]) : 1;
                    mc.interactionManager.clickCreativeStack(new net.minecraft.item.ItemStack(item, count), slot);
                    result = "set slot " + slot + " = " + a[2] + " x" + count;
                    break;
                }
                case "place": case "use": {
                    // Right-click a block face: places the held block onto it, OR (for a container like a chest)
                    // opens it - which is how a chest's block entity + inventory get materialized server-side.
                    // `use <x> <y> <z> [face]` (face defaults to up). Builds a real Use Item On packet.
                    int x = Integer.parseInt(a[1]), y = Integer.parseInt(a[2]), z = Integer.parseInt(a[3]);
                    var face = a.length > 4 ? net.minecraft.util.math.Direction.byName(a[4].toLowerCase())
                                            : net.minecraft.util.math.Direction.UP;
                    var bpos = new net.minecraft.util.math.BlockPos(x, y, z);
                    var hit = new net.minecraft.util.hit.BlockHitResult(
                        net.minecraft.util.math.Vec3d.ofCenter(bpos), face, bpos, false);
                    var action = mc.interactionManager.interactBlock(mc.player, Hand.MAIN_HAND, hit);
                    mc.player.swingHand(Hand.MAIN_HAND);
                    result = "use (" + x + " " + y + " " + z + " " + face + ") -> " + action;
                    break;
                }
                case "exit": {
                    result = "exiting";
                    mc.scheduleStop();
                    break;
                }
                default:
                    result = "unknown command: " + a[0];
            }
        } catch (Throwable t) {
            result = "error: " + t;
        }

        PacketByteBuf out = PacketByteBufs.create();
        out.writeString(result);
        ClientPlayNetworking.send(CHANNEL, out);
    }
}
