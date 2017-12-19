﻿namespace BrainNetwork.BrainDeviceProtocol
{
    public struct BrainDevState
    {
        public byte DevCode;
        public byte ChannelCount;
        public byte Gain;//放大倍数
        public SampleRateEnum SampleRate;
        public TrapSettingEnum TrapOption;
        public bool EnalbeFilter;
        public bool IsStart;
        
        public const int StoreSize = 6;
        
        public const int SampleCount_250 = 20 * 250 / 1000;
        public const int SampleCount_500 = 20 * 500 / 1000;
        public const int SampleCount_1k = 20 * 1000 / 1000;
        public const int SampleCount_2k = 20 * 2000 / 1000;

        public static int SampleCountPer20ms(SampleRateEnum sampleRate)
        {
            var count = 1;
            switch (sampleRate)
            {
                case SampleRateEnum.SPS_250: //every 1000ms sample 250 times
                    count = BrainDevState.SampleCount_250; //20ms -> sample counts
                    break;
                case SampleRateEnum.SPS_500:
                    count = BrainDevState.SampleCount_500; //20ms -> sample counts
                    break;
                case SampleRateEnum.SPS_1k:
                    count = BrainDevState.SampleCount_1k; //20ms -> sample counts
                    break;
                case SampleRateEnum.SPS_2k:
                    count = BrainDevState.SampleCount_2k; //20ms -> sample counts
                    break;
            }
            return count;
        }
        
        public override string ToString()
        {
            return $"{nameof(DevCode)}: {DevCode}, {nameof(ChannelCount)}: {ChannelCount},  {nameof(Gain)}: {Gain}, {nameof(SampleRate)}: {SampleRate}, {nameof(TrapOption)}: {TrapOption}, {nameof(EnalbeFilter)}: {EnalbeFilter}, {nameof(IsStart)}: {IsStart}";
        }
    }

    public enum TrapSettingEnum
    {
        NoTrap,
        Trap_50,
        Trap_60,
    }

    public enum SampleRateEnum
    {
        SPS_250=1,
        SPS_500,
        SPS_1k,
        SPS_2k,
    }
}