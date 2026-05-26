namespace Hanzo.AI
{
    public static class AINameCatalog
    {
        private static readonly string[] Names =
        {
            "HanzoX",
            "Yazoo@",
            "C0pyN1nja",
            "PayDaPr1ce",
            "LootBag8",
            "Sc4tterSh0t",
            "~Bull3tTime",
            "Fr4g_Out",
            "TriggerMan_77",
            "CoinOp7",
            "0dins_B3ard",
            "Susano0",
            "8Bit_Ninja",
            "PixelPwn3r",
            "R3coil_Ruler",
            "StealthStr1ke",
            "ShadowSn1per",
            "N1njaL33t",
            "G4m3Ov3r",
            "D3athM4rk",
            "L33tSn1p3r",
            "R3load_R3bel",
            "Turb0_Leopard",
            "1dleUnit",
            "C0mmand3r",
            "S3rgeant_Stealth",
            "R3con_R3bel",
            "N1njaAss4ss1n",
            "G4m3M4st3r",
            "D3athBr1ng3r",
            "L33tSn1p3r",
            "WarGeneral88",
            "NightWatch0",
            "70P_Shatter7"
        };

        public static string GetNameForId(int aiId)
        {
            int index = System.Math.Abs(aiId);
            if (index <= 0)
                index = 1;

            return Names[(index - 1) % Names.Length];
        }

        public static bool TryGetIdFromPrefabName(string source, out int aiId)
        {
            aiId = 0;

            if (string.IsNullOrWhiteSpace(source))
                return false;

            const string prefix = "AIPlayer";
            const string cloneSuffix = "(Clone)";

            string prefabName = source.Trim();
            if (prefabName.EndsWith(cloneSuffix, System.StringComparison.OrdinalIgnoreCase))
                prefabName = prefabName.Substring(0, prefabName.Length - cloneSuffix.Length).Trim();

            if (!prefabName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix = prefabName.Substring(prefix.Length);
            if (suffix.Length == 0)
                return false;

            char separator = suffix[0];
            if (separator != '_' && separator != '-' && !char.IsWhiteSpace(separator))
                return false;

            suffix = suffix.Substring(1).Trim();
            if (!int.TryParse(suffix, out int index) || index <= 0)
                return false;

            aiId = -index;
            return true;
        }
    }
}
