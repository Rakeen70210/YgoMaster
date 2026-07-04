namespace YgoMaster
{
    enum DuelRoomTableState
    {
        None,
        Joinable = 1,
        P1StandingBy = 2,
        P2StandingBy = 3,
        Matched = 4,
        Dueling = 5
    }

    class Player
    {
        public uint Code;
        public string Name;
        public DuelRoom DuelRoom;
        public DuelSettings ActiveDuelSettings = new DuelSettings();
        public uint SpectatingPlayerCode;
    }

    class DuelSettings
    {
        public long did;
        public int MyID;

        public void CopyFrom(DuelSettings other)
        {
            if (other != null)
            {
                did = other.did;
                MyID = other.MyID;
            }
        }
    }
}

namespace YgoMaster.Net
{
    class NetClient
    {
        public bool Closed { get; private set; }

        public void Close()
        {
            Closed = true;
        }
    }
}
