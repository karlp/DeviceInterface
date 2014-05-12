﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DeviceMemories;
using ECore.DataPackages;
using System.IO;
using ECore.HardwareInterfaces;


namespace ECore.Devices
{
    //this is the main class which fills the EDevice with data specific to the HW implementation.
    //eg: which memories, which registers in these memories, which additional functionalities, the start and stop routines, ...
    public partial class ScopeV2 : EDevice, IScope
    {
        public EDeviceHWInterface hardwareInterface;
        private ScopeConnectHandler scopeConnectHandler;

        public DeviceMemories.ScopeFpgaSettingsMemory FpgaSettingsMemory { get; private set; }
        public DeviceMemories.ScopeFpgaRom FpgaRom { get; private set; }
        public DeviceMemories.ScopeStrobeMemory StrobeMemory { get; private set; }
        public DeviceMemories.MAX19506Memory AdcMemory { get; private set; }
        public DeviceMemories.ScopePicRegisterMemory PicMemory { get; private set; }

        private DataSources.DataSourceScope dataSourceScope;
        public DataSources.DataSourceScope DataSourceScope { get { return dataSourceScope; } }

        private float[] calibrationCoefficients = new float[] { 0.0042f, -0.0029f, 0.1028f };

        private bool disableVoltageConversion;
        private const double SAMPLE_PERIOD = 10e-9;
        private const uint NUMBER_OF_SAMPLES = 2048;

#if ANDROID
		public Android.Content.Res.AssetManager Assets;
#endif

        public ScopeV2(ScopeConnectHandler handler)
            : base()
        {
            //figure out which yOffset value needs to be put in order to set a 0V signal to midrange of the ADC = 128binary
            //FIXME: no clue why this line is here...
            //yOffset_Midrange0V = (int)((0 - 128f * calibrationCoefficients[0] - calibrationCoefficients[2]) / calibrationCoefficients[1]);
            this.scopeConnectHandler += handler;
            dataSourceScope = new DataSources.DataSourceScope(this);
            InitializeHardwareInterface();
        }

        #region initializers

        private void InitializeHardwareInterface()
        {
#if ANDROID
		hardwareInterface = new HardwareInterfaces.HWInterfacePIC_Xamarin(this);
#else
            HWInterfacePIC_LibUSB hwPic = new HardwareInterfaces.HWInterfacePIC_LibUSB(OnDeviceConnect);
            hardwareInterface = hwPic;
            hwPic.CheckForDevices();
#endif
        }

        private void OnDeviceConnect(bool connected)
        {
            //Flash FPGA
            //FIXME: I have to do this synchronously here because there's no blocking on the USB traffic
            //but there should be when flashing the FPGA.
            if (connected)
            {
                hardwareInterface.WriteControlBytes(new byte[] { 123, 1 });
                byte[] response = hardwareInterface.ReadControlBytes(16);
                string resultString = "PIC FW Version readout (" + response.Length.ToString() + " bytes): ";
                foreach (byte b in response)
                    resultString += b.ToString() + ";";
                Logger.AddEntry(this, LogLevel.Debug, resultString);
				LogWait("Starting fpga flashing...", 0);
                FlashFpgaInternal();
				LogWait("FPGA flashed...");
                InitializeMemories();
				LogWait("Memories initialized...");
                FpgaRom.ReadSingle(ROM.FW_MSB);
                FpgaRom.ReadSingle(ROM.FW_LSB);
                Logger.AddEntry(this, LogLevel.Debug, "FPGA ROM MSB:LSB = " + FpgaRom.GetRegister(ROM.FW_MSB).GetByte() + ":" + FpgaRom.GetRegister(ROM.FW_LSB).GetByte());
            }
            if (scopeConnectHandler != null)
                scopeConnectHandler(this, connected);
        }

        //master method where all memories, registers etc get defined and linked together
        private void InitializeMemories()
        {
            //Create memories
            IScopeHardwareInterface scopeInterface = (IScopeHardwareInterface)hardwareInterface;
            PicMemory = new DeviceMemories.ScopePicRegisterMemory(scopeInterface);
            FpgaSettingsMemory = new DeviceMemories.ScopeFpgaSettingsMemory(scopeInterface);
            FpgaRom = new DeviceMemories.ScopeFpgaRom(scopeInterface);
            StrobeMemory = new DeviceMemories.ScopeStrobeMemory(FpgaSettingsMemory, FpgaRom);
            AdcMemory = new DeviceMemories.MAX19506Memory(FpgaSettingsMemory, StrobeMemory, FpgaRom);
            //Add them in order we'd like them in the GUI

            memories.Add(FpgaRom);
            memories.Add(FpgaSettingsMemory);
            memories.Add(AdcMemory);
            memories.Add(PicMemory);
            memories.Add(StrobeMemory);
        }

        #endregion

        #region start_stop

		private void LogWait(string message, int sleep = 0)
        {
            Logger.AddEntry(this, LogLevel.Debug, message);
			System.Threading.Thread.Sleep(sleep);
        }

        public void Configure()
        {

            //raise global reset
            StrobeMemory.GetRegister(STR.GLOBAL_NRESET).Set(false);
            StrobeMemory.WriteSingle(STR.GLOBAL_NRESET);
            LogWait("FPGA reset");
            //set feedback loopand to 1V for demo purpose and enable
            SetDivider(0, 1);
            SetDivider(1, 1);
            LogWait("dividers to 1");

            //FIXME: these are byte values, since the setter helper is not converting volt to byte
            this.SetYOffset(0, 0f);
            this.SetYOffset(1, 0f);
            LogWait("yoffset to zero");

            //set ADC to offset binary output (required for FPGA triggering)
            AdcMemory.GetRegister(MAX19506.POWER_MANAGEMENT).Set(4);
            AdcMemory.WriteSingle(MAX19506.POWER_MANAGEMENT);
            LogWait("ADC pwr mgmgt (4)");

            AdcMemory.GetRegister(MAX19506.FORMAT_PATTERN).Set(16);
            AdcMemory.WriteSingle(MAX19506.FORMAT_PATTERN);
            LogWait("ADC format patt");

            //Set ADC multiplexed output mode
            AdcMemory.GetRegister(MAX19506.OUTPUT_FORMAT).Set(0x02); //DDR on chA
            AdcMemory.WriteSingle(MAX19506.OUTPUT_FORMAT);
            LogWait("ADC Output format");

            AdcMemory.GetRegister(MAX19506.CHA_TERMINATION).Set(27);
            AdcMemory.WriteSingle(MAX19506.CHA_TERMINATION);
            LogWait("ADC CHA term");

            AdcMemory.GetRegister(MAX19506.DATA_CLK_TIMING).Set(24);
            AdcMemory.WriteSingle(MAX19506.DATA_CLK_TIMING);
            LogWait("ADC DCLK timing");

            //Enable scope controller
            StrobeMemory.GetRegister(STR.SCOPE_ENABLE).Set(true);
            StrobeMemory.WriteSingle(STR.SCOPE_ENABLE);
            LogWait("Scope enable");

            //lower global reset
            LogWait("Waiting to get device out of reset...", 2000);
            StrobeMemory.GetRegister(STR.GLOBAL_NRESET).Set(true);
            StrobeMemory.WriteSingle(STR.GLOBAL_NRESET);
            LogWait("Ended reset");

            StrobeMemory.GetRegister(STR.ENABLE_ADC).Set(true);
            StrobeMemory.WriteSingle(STR.ENABLE_ADC);
            LogWait("ADC clock enabled", 2000);

            StrobeMemory.GetRegister(STR.ENABLE_RAM).Set(true);
            StrobeMemory.WriteSingle(STR.ENABLE_RAM);
            LogWait("RAM enabled", 2000);


            StrobeMemory.GetRegister(STR.ENABLE_NEG_DCDC).Set(true);
            StrobeMemory.WriteSingle(STR.ENABLE_NEG_DCDC);
            LogWait("Enable neg dcdc");
        }

        #endregion

        #region data_handlers

        public byte[] GetBytes()
        {
            int samplesToFetch = 4096;
            int bytesToFetch = samplesToFetch;
            return hardwareInterface.GetData(bytesToFetch);
        }

        private float[] ConvertByteToVoltage(byte[] buffer, byte yOffset)
        {
            float[] voltage = new float[buffer.Length];

            //this section converts twos complement to a physical voltage value
            float totalOffset = (float)yOffset * calibrationCoefficients[1] + calibrationCoefficients[2];
            for (int i = 0; i < buffer.Length; i++)
            {
                float gainedVal = (float)buffer[i] * calibrationCoefficients[0];
                voltage[i] = gainedVal + totalOffset;
            }
            return voltage;
        }

        public DataPackageScope GetScopeData()
        {
            byte[] buffer = this.GetBytes();
            if (buffer == null) return null;
            //FIXME: Get these scope settings from header
            double samplePeriod = 10e-9; //10ns -> 100MHz fixed for now
            int triggerIndex = 0;


            //Split in 2 channels
            byte[] chA = new byte[buffer.Length / 2];
            byte[] chB = new byte[buffer.Length / 2];
            for (int i = 0; i < chA.Length; i++)
            {
                chB[i] = buffer[2 * i];
                chA[i] = buffer[2 * i + 1];
            }

            //construct data package
            //FIXME: get firstsampletime and samples from FPGA
            DataPackageScope data = new DataPackageScope(samplePeriod, triggerIndex, chA.Length, 0);
            //FIXME: parse package header and set DataPackageScope's trigger index
            //FIXME: Get bytes, split into analog/digital channels and add to scope data
            if (this.disableVoltageConversion)
            {
                data.SetData(ScopeChannels.ChA, Utils.CastArray<byte, float>(chA));
                data.SetData(ScopeChannels.ChB, Utils.CastArray<byte, float>(chB));
            }
            else
            {
                //FIXME: shouldn't the register here be CHA_YOFFSET_VOLTAGE?
                data.SetData(ScopeChannels.ChA,
                    ConvertByteToVoltage(chA, FpgaSettingsMemory.GetRegister(REG.CHB_YOFFSET_VOLTAGE).GetByte()));

                //Check if we're in LA mode and fill either analog channel B or digital channels
                if (!this.GetEnableLogicAnalyser())
                {
                    data.SetData(ScopeChannels.ChB,
                        ConvertByteToVoltage(chB, FpgaSettingsMemory.GetRegister(REG.CHB_YOFFSET_VOLTAGE).GetByte()));
                }
                else
                {
                    data.SetDataDigital(chB);
                }
            }
            return data;
        }

        public override bool Connected { get { return hardwareInterface.Connected; } }

        #endregion
    }
}
