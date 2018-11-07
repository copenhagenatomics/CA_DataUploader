using CA_DataUploaderLib;
using LoopComponents;
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
        private CAThermalBox _hub;
        private Font _fontNormal;
        private Font _fontSmall;
        private StringFormat _format = new StringFormat();

        public Form1()
        {
            InitializeComponent();

            var serial = new SerialNumberMapper(true);
            var dataLoggers = serial.ByFamily("Temperature");
            if (dataLoggers.Any())
            {
                timer1.Enabled = true;
                _hub = new CAThermalBox(dataLoggers.First().portName, 4, 1);
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
            pictureBox1.Image = bmp;
        }

        private List<JackRectangle> Draw4Boxes(Graphics g, int width, int height)
        {
            double lineHeight = height / 16.0;
            double colWidth = width / 10.5;

            var list = _hub.GetAllValidTemperatures().OrderBy(x => x.Hub).Select(x => new JackRectangle(x, colWidth, lineHeight)).ToList();
            g.FillRectangle(Brushes.LightGray, 0, 0, width, height);
            foreach(var jack in list)
            {
                if (jack.Sensor.Temperature < 1000)
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
            }

            var pos = new Point(width / 2, height - 10);
            g.DrawString("Frequency: " + _hub.Frequency.ToString("N1") + " Hz", _fontSmall, Brushes.Black, pos, _format);

            return list;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _hub.Dispose();
            timer1.Enabled = false;
        }

        private void textBox1_Leave(object sender, EventArgs e)
        {
            // _hub.FilterLength = (int)(int.Parse(textBox1.Text) * _hub.Frequency);
        }
    }
}
