﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DataPackages;

#if IPHONE || ANDROID
#else
using System.Windows.Forms;
using MatlabFileIO;
#endif

namespace ECore.DataSources
{
    public class DataSourceFile: DataSource
    {
        private int sleepTime = 10;

#if IPHONE || ANDROID
		public EDataNodeFromFile()
		{
		}

		public override EDataPackage LatestDataPackage
		{
			get { 
				//return null;
				float[] dummyData = new float[4096];
				Random rand = new Random ();
				for (int i = 0; i < dummyData.Length; i++) {
					dummyData [i] = (float) rand.Next (200)+50;
				}
				lastDataPackage = new EDataPackage (dummyData);
				return lastDataPackage;
			}
		}

		public override void Update(EDataNode sender, EventArgs e)
		{
		}
#else
		MatlabFileReader fileReader;
		MatlabFileArrayReader arrayReader;

		public DataSourceFile()
        {
            //show select file dialog
            //FIXME: NO GUI IN ECORE!!!
            OpenFileDialog dialog = new OpenFileDialog { Filter = @"data files (*.mat)|*.mat", Title = @"Select a saved data stream"};

            //if strange things happened
            if (dialog.ShowDialog() != DialogResult.OK)
                throw new Exception("Please do a better job selecting a data file");

            //open selected file
            fileReader = new MatlabFileReader(dialog.FileName);
            OpenArray();
            lastUpdate = DateTime.Now;
        }
        
        private void OpenArray()
        {
            arrayReader = fileReader.OpenArray("ScopeData");
        }
        public override bool Update()
        {
            //since this is a source node, it should fire its event at a certain interval.
            //in order to emulate this, thread will be suspended.
            DateTime now = DateTime.Now;
            int slackTime = sleepTime - (int)now.Subtract(lastUpdate).TotalMilliseconds;


            if (slackTime > 0)
                System.Threading.Thread.Sleep(slackTime);
            lastUpdate = DateTime.Now;
            

            //check whether stream needs to be rewound
            if (arrayReader.CurrentRow >= arrayReader.TotalRows)
                OpenArray();

            //read data from file
            float[] voltageValues = arrayReader.ReadRowFloat();            

            //convert data into an EDataPackage
            latestDataPackage = new DataPackageWaveAnalog(voltageValues, 0);
            return true;
        }
#endif
    }
}