package sharptester.mixin;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.DisconnectedScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Exits the client whenever the disconnected / "Back to server list" screen appears — i.e. on a FAILED
 * connection (connection refused, server still starting) or a kick. Neither fires the play-disconnect event the
 * mod otherwise stops on (that only covers an established PLAY session dropping), so without this the instance
 * parks on the screen and a single-instance Prism hands the next launch back to it instead of reconnecting. With
 * this, a failed run frees the instance immediately, like a clean disconnect does.
 */
@Mixin(DisconnectedScreen.class)
public class DisconnectedScreenMixin {
    @Inject(method = "init", at = @At("TAIL"))
    private void sharptester$exitOnDisconnectScreen(CallbackInfo ci) {
        System.err.println("[SharpTester] disconnected screen shown — stopping client");
        MinecraftClient.getInstance().scheduleStop();
    }
}
