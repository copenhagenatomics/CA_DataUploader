using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CA_AverageTemperature
{
    public partial class Form1 : Form
    {
        private BaseSensorBox _hub;
        private Font _fontNormal;
        private Font _fontSmall;
        private Bitmap _currentBmp;
        private StringFormat _format = new StringFormat();

        public Form1()
        {
            InitializeComponent();
            var serial = new SerialNumberMapper();
            var dataLoggers = serial.ByProductType("Temperature");
            if (dataLoggers.Any())
            {
                timer1.Enabled = true;
                _hub = new ThermocoupleBox(null);
            }
            else
            {
                MessageBox.Show("Tempearture sensors not initialized");
            }

            _format.LineAlignment = StringAlignment.Center;
            _format.Alignment = StringAlignment.Center;
            _fontNormal = new Font("Cambria", 16.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fontSmall = new Font("Cambria", 12.0f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var bmp = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                var boxRects = Draw4Boxes(g, pictureBox1.Width, pictureBox1.Height);
                
            }

            if (_currentBmp != null)
            {
                _currentBmp.Dispose();
                _currentBmp = null;
            }

            _currentBmp = bmp;
            pictureBox1.Image = bmp;
        }

        private List<JackRectangle> Draw4Boxes(Graphics g, int width, int height)
        {
            var list = _hub.GetValues().OrderBy(x => x.Input.PortNumber).Select(x => new JackRectangle(x, width, height, "1x10", 1)).ToList();
            g.FillRectangle(Brushes.LightGray, 0, 0, width, height);
            foreach(var jack in list)
            {
                if (jack.Sensor.Value < 1000)
                {
                    g.FillRectangle(Brushes.LightGreen, jack.Rect);
                    g.DrawString(jack.Text, _fontNormal, Brushes.Black, jack.TextPos, _format);
                }
                else
                {
                    g.FillRectangle(Brushes.PaleVioletRed, jack.Rect);
                    g.DrawString(jack.Text, _fontSmall, Brushes.Black, jack.TextPos, _format);
                }

                g.DrawRectangle(new Pen(Color.Black, 1), jack.Rect);

                if (jack.TitlePos.X > 0)
                {
                    g.DrawString("Internal temperature", _fontSmall, Brushes.Black, jack.TitlePos, _format);
                    g.DrawString("Serial: " + jack.Sensor.Input.Map.Board.serialNumber, _fontSmall, Brushes.Black, jack.SerialPos, _format);
                }
            }

            var pos = new Point(width / 2, height - 10);
            g.DrawString("Frequency: " + "unknown" + " Hz", _fontSmall, Brushes.Black, pos, _format);

            return list;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(_hub != null)
                _hub.Dispose();
            timer1.Enabled = false;
        }
    }
}
