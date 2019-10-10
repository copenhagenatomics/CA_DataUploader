using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using System.Diagnostics;

namespace UnitTests
{
    [TestClass]
    public class SerialPortTests
    {
        [TestMethod]
        public void Scale()
        {
            //Set the scale in RS232 mode by holding MODE until display show Prt, then press TARA. 
            //Then press MODE until you see 'Cont in', then press TARA. Then connect to COM port with 9600 baud.

            var mapper = new SerialNumberMapper(true);
            var list = mapper.ByProductType("Scale");

            for(int i=0; i<50; i++)
            {
                string str = string.Empty;
                foreach(var scale in list)
                {
                    str += scale.ReadLine().Replace("\r", "      ");
                }

                Debug.Print(str);
            }
        }
    }
}
