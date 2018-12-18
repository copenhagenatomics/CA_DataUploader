using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CA_AverageTemperature
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Could not load file or assembly 'Microsoft.VisualBasic"))
                    Console.WriteLine("Please install: sudo apt-get install mono-vbnc");

                if (e.InnerException == null)
                    MessageBox.Show(e.ToString());
                else
                    MessageBox.Show(e.InnerException.ToString());
            }
        }
    }
}
