using System;

namespace Shortcut.RustMod
{
    // BEGIN SHARED SKATE PARK BUILD ITEMS
    public enum SkateParkBuildItemState { None, Preparing, Paid, RecoveryClaimed }

    public static class SkateParkBuildItems
    {
        public const string Carrier = "building.planner";
        public const int MarkerBase = 0x534B5000;
        public const int PieceCount = 8;

        public static string Name(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: return "Skate Bank";
                case SkateParkKind.Kicker: return "Skate Kicker";
                case SkateParkKind.QuarterPipe: return "Skate Quarterpipe";
                case SkateParkKind.LedgeLow: return "Skate Low Ledge";
                case SkateParkKind.LedgeHigh: return "Skate High Ledge";
                case SkateParkKind.LongLedge: return "Skate Long Ledge";
                case SkateParkKind.Funbox: return "Skate Funbox";
                case SkateParkKind.Halfpipe: return "Skate Halfpipe";
                default: return null;
            }
        }

        public static bool Tagged(int marker)
        { return marker >= MarkerBase && marker <= MarkerBase + 255; }

        public static int Marker(SkateParkKind kind)
        { return Name(kind) == null ? 0 : MarkerBase + (int)kind + 1; }

        public static bool TryKind(int marker, out SkateParkKind kind)
        {
            kind = default(SkateParkKind);
            if (!Tagged(marker)) return false;
            int index = marker - MarkerBase - 1;
            if (index < 0 || index >= PieceCount) return false;
            kind = (SkateParkKind)index;
            return Name(kind) != null;
        }

        public static bool TryIdentity(string carrier, ulong skin, int marker, string name,
            int amount, out SkateParkKind kind)
        {
            kind = default(SkateParkKind);
            SkateParkKind parsed;
            if (carrier != Carrier || skin != 0 || amount != 1 || !TryKind(marker, out parsed) ||
                !string.Equals(name, Name(parsed), StringComparison.Ordinal)) return false;
            kind = parsed;
            return true;
        }

        public static bool VerifiedDebit(int before, int after, bool sameItem)
        { return sameItem && before == 1 && after == 0; }

        public static bool Recoverable(SkateParkBuildItemState state, int members, SkateParkKind kind)
        {
            return state == SkateParkBuildItemState.Paid && Name(kind) != null &&
                members == SkateParkLayout.PartCount(kind);
        }

        public static bool ValidState(SkateParkBuildItemState state)
        { return (int)state >= 0 && (int)state <= (int)SkateParkBuildItemState.RecoveryClaimed; }
    }
    // END SHARED SKATE PARK BUILD ITEMS
}
