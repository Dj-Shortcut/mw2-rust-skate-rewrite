using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ShortcutSkateProbe", "Dj-Shortcut", "0.1.0")]
    [Description("Administrator-only client echo diagnostic.")]
    public sealed class ShortcutSkateProbe : RustPlugin
    {
        private const string Usage = "Usage: skate.probe <sequence> (one positive whole number, at most ten digits).";

        [ConsoleCommand("skate.probe")]
        private void Probe(ConsoleSystem.Arg arg)
        {
            if (arg == null) return;
            BasePlayer player = arg.Player();
            if (player == null || !player.IsConnected || player.IsNpc || !player.IsAdmin)
            {
                arg.ReplyWith("This command requires a connected in-game server administrator.");
                return;
            }
            uint sequence;
            if (arg.Args == null || arg.Args.Length != 1 || !TrySequence(arg.GetString(0), out sequence))
            {
                arg.ReplyWith(Usage);
                return;
            }
            player.SendConsoleCommand("echo", "SKATE-PROBE " + sequence.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TrySequence(string token, out uint sequence)
        {
            sequence = 0;
            if (string.IsNullOrEmpty(token) || token.Length > 10) return false;
            for (int i = 0; i < token.Length; i++)
                if (token[i] < '0' || token[i] > '9') return false;
            return uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence != 0;
        }
    }
}
