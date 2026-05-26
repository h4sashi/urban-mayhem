namespace Hanzo.AI
{
    public struct AIAdaptiveTuning
    {
        public float matchPressure;
        public float recentDamagePressure;
        public float lowHealthPressure;
        public int activeAICount;

        public static AIAdaptiveTuning Neutral
        {
            get
            {
                return new AIAdaptiveTuning
                {
                    matchPressure = 0f,
                    recentDamagePressure = 0f,
                    lowHealthPressure = 0f,
                    activeAICount = 1,
                };
            }
        }
    }
}
