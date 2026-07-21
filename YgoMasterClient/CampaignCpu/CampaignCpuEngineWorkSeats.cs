using System;
using System.Runtime.InteropServices;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Reads DoCommandUser / RunDialogUser from local duel.dll engine work memory
    /// using the same offsets as PvP (ClientSettings override or CampaignCpuDefaults).
    /// </summary>
    static class CampaignCpuEngineWorkSeats
    {
        public static IntPtr EngineWorkBase;

        public static int EffectiveDoCommandUserOffset
        {
            get
            {
                int o = ClientSettings.CampaignCpuEngineWorkDoCommandUserOffset;
                return o != 0 ? o : CampaignCpuDefaults.DoCommandUserOffset;
            }
        }

        public static int EffectiveRunDialogUserOffset
        {
            get
            {
                int o = ClientSettings.CampaignCpuEngineWorkRunDialogUserOffset;
                return o != 0 ? o : CampaignCpuDefaults.RunDialogUserOffset;
            }
        }

        public static bool TryReadSeats(out int doCommandUser, out int runDialogUser)
        {
            doCommandUser = -1;
            runDialogUser = -1;
            if (EngineWorkBase == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                doCommandUser = Marshal.ReadInt32(EngineWorkBase, EffectiveDoCommandUserOffset);
                runDialogUser = Marshal.ReadInt32(EngineWorkBase, EffectiveRunDialogUserOffset);
                if (doCommandUser == unchecked((int)0xFFFFFFFF))
                {
                    doCommandUser = -1;
                }
                if (runDialogUser == unchecked((int)0xFFFFFFFF))
                {
                    runDialogUser = -1;
                }
                return true;
            }
            catch
            {
                doCommandUser = -1;
                runDialogUser = -1;
                return false;
            }
        }
    }
}
