package sharptester.mixin;

import net.minecraft.client.network.ClientPlayNetworkHandler;
import net.minecraft.network.packet.s2c.play.GameMessageS2CPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Captures the text of each system/game chat message the client receives, so the harness's {@code chatlast}
 * command can assert that the server's chat components actually parsed and rendered (a malformed component
 * would instead throw in the client's decoder). Stored on {@link sharptester.SharpTester#lastChat}.
 */
@Mixin(ClientPlayNetworkHandler.class)
public class ClientPlayNetworkHandlerMixin {
    @Inject(method = "onGameMessage", at = @At("HEAD"))
    private void sharptester$captureChat(GameMessageS2CPacket packet, CallbackInfo ci) {
        sharptester.SharpTester.lastChat = packet.content().getString();
    }
}
