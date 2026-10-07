package eaglerrelay.mixin;

import java.net.InetAddress;
import java.net.InetSocketAddress;

import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/**
 * Sends every connection to a remote server to the launcher's local relay port
 * (-Deaglerrelay.port). The launcher reads the target from the Minecraft handshake
 * and tunnels the TCP stream over a WebSocket relay, like the browser version.
 * Local and LAN addresses are left untouched; without the property this is a no-op.
 */
@Mixin(targets = "net.minecraft.network.Connection")
public abstract class ConnectionMixin {
	@ModifyVariable(method = "connect", at = @At("HEAD"), argsOnly = true)
	private static InetSocketAddress eaglerrelay$route(InetSocketAddress address) {
		String port = System.getProperty("eaglerrelay.port");
		if (port == null || address == null) return address;
		InetAddress ip = address.getAddress();
		if (ip != null && (ip.isLoopbackAddress() || ip.isSiteLocalAddress() || ip.isLinkLocalAddress() || ip.isAnyLocalAddress())) {
			return address;
		}
		try {
			return new InetSocketAddress(InetAddress.getLoopbackAddress(), Integer.parseInt(port));
		} catch (NumberFormatException e) {
			return address;
		}
	}
}
