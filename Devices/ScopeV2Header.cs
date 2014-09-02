﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DeviceMemories;
using Common;

namespace ECore.Devices
{
    internal class SmartScopeHeader
    {
        private byte[] raw;
        internal int NumberOfPayloadBursts { get; private set; }
        internal int PackageOffset { get; private set; }
        internal int PackageSize { get; private set; }
        internal byte BytesPerBurst { get; private set; }
        
        internal int Samples { get; private set; }
        internal double SamplePeriod { get; private set; }
        
        internal bool ScopeRunning { get; private set; }
        internal bool Rolling { get; private set; }
        internal readonly int Channels = 2;
        internal int TriggerAddress { get; private set; }
        
        internal SmartScopeHeader(byte[] data)
        {
            int headerSize = AcquisitionRegisters.Length + DumpRegisters.Length + (int)Math.Ceiling(AcquisitionStrobes.Length / 8.0);
            raw = new byte[headerSize];
            if (data[0] != 'L' || data[1] != 'N')
                throw new Exception("Invalid magic number, can't parse header");

            int headerOffset = data[2];
            Array.Copy(data, headerOffset, raw, 0, headerSize);
            
            BytesPerBurst = data[3];
            NumberOfPayloadBursts = data[4] + (data[5] << 8);
            //FIXME: this should be corrected in FW
            if (NumberOfPayloadBursts == 0)
                NumberOfPayloadBursts = 64;
            PackageOffset = (short)(data[6] + (data[7] << 8));
            PackageSize = (short)(data[8] + (data[9] << 8));
            
            Samples = NumberOfPayloadBursts * BytesPerBurst / Channels;
            ScopeRunning = Utils.IsBitSet(data[10], 0);
            Rolling = Utils.IsBitSet(data[10], 1);

            TriggerAddress = data[11] + (data[12] << 8) + (data[13] << 16);
            //FIXME: REG_VIEW_DECIMATION disabled (always equals ACQUISITION_MULTIPLE_POWER)
            //SamplePeriod = 10e-9 * Math.Pow(2, GetRegister(REG.VIEW_DECIMATION) + GetRegister(REG.INPUT_DECIMATION));
            //For now, we hardcoded this case in the FPGA
            SamplePeriod = 10e-9 * Math.Pow(2, GetRegister(REG.ACQUISITION_MULTIPLE_POWER) + GetRegister(REG.INPUT_DECIMATION));
        }

        internal byte GetRegister(REG r)
        {
            int offset = 0;
            if (!AcquisitionRegisters.Contains(r))
            {
                if (!DumpRegisters.Contains(r))
                    throw new Exception("Register " + r.ToString("G") + " not part of header");
                else
                    offset = AcquisitionRegisters.Length + Array.IndexOf(DumpRegisters, r);;
            }
            else
            {
                offset = Array.IndexOf(AcquisitionRegisters, r);
            }

            return raw[offset]; 
        }

        internal bool GetStrobe(STR s)
        {
            if(!AcquisitionStrobes.Contains(s))
                throw new Exception("Strobe  " + s.ToString("G") + " not part of header");
            int offset = AcquisitionRegisters.Length + DumpRegisters.Length;
            offset += Array.IndexOf(AcquisitionStrobes, s) / 8;
            return Utils.IsBitSet(raw[offset], (int)Array.IndexOf(AcquisitionStrobes, s) % 8);
        }
        /*
         * WARNING: the following arrays are manually constructed from VHDL code in
         * TypesConstants.vhd - Make sure that when you change the VHDL, you also
         * update this code
         */
        internal static readonly REG[] AcquisitionRegisters = new REG[]
        {
            REG.TRIGGER_LEVEL, 
            REG.TRIGGER_MODE,
            REG.TRIGGER_WIDTH,
			REG.TRIGGERHOLDOFF_B0, 
            REG.TRIGGERHOLDOFF_B1, 
            REG.TRIGGERHOLDOFF_B2, 
            REG.TRIGGERHOLDOFF_B3, 
			REG.CHA_YOFFSET_VOLTAGE, 
			REG.CHB_YOFFSET_VOLTAGE, 
			REG.DIVIDER_MULTIPLIER,
			REG.INPUT_DECIMATION, 
            REG.ACQUISITION_MULTIPLE_POWER,
            REG.TRIGGER_THRESHOLD,
            REG.TRIGGER_PWM,
			REG.DIGITAL_TRIGGER_RISING,
			REG.DIGITAL_TRIGGER_FALLING,
			REG.DIGITAL_TRIGGER_HIGH,
			REG.DIGITAL_TRIGGER_LOW
        };
        internal static readonly REG[] DumpRegisters = new REG[]
        {
            //FIXME: REG_VIEW_DECIMATION disabled (always equals ACQUISITION_MULTIPLE_POWER)
			//REG.VIEW_DECIMATION,
			REG.VIEW_OFFSET,	
			REG.VIEW_ACQUISITIONS,
			REG.VIEW_BURSTS
        };
        internal static readonly STR[] AcquisitionStrobes = new STR[]
        {
			STR.AWG_ENABLE,
			STR.LA_ENABLE,
			STR.CHA_DCCOUPLING,
			STR.CHB_DCCOUPLING,
            STR.DEBUG_RAM,
            STR.DIGI_DEBUG,
            STR.ROLL,
            STR.LA_CHANNEL
        };
    }
}
