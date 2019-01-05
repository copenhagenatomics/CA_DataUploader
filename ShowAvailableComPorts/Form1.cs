using CA_DataUploaderLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ShowAvailableComPorts
{
    public partial class Form1 : Form
    {
        private SerialNumberMapper _ports = new SerialNumberMapper(false);
        private MCUBoard _selectedBoard;
        private bool _running = true;

        public Form1()
        {
            InitializeComponent();
            new Thread(() => this.LoopForever()).Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            listBox1.DataSource = _ports.McuBoards.Select(x => x.ToString(", ")).ToList();
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBox1.Clear();
            var str = listBox1.SelectedItem.ToString();
            _selectedBoard = _ports.McuBoards.First(x => x.ToString(", ") == str);
        }

        private void LoopForever()
        {
            while (_running)
            {
                if (_selectedBoard != null && _selectedBoard.IsOpen)
                    SetText(_selectedBoard.ReadLine());

                Thread.Sleep(50);
            }
        }

        private void SetText(string line)
        {
            if (this.InvokeRequired)
                this.BeginInvoke(new Action<string>(SetText), line);
            else
            {
                if (textBox1.Text.Length > 2000)
                    textBox1.Clear();

                textBox1.Text = line + Environment.NewLine + textBox1.Text;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _running = false;
            foreach (var port in _ports.McuBoards)
                port.Close();
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (_selectedBoard != null && _selectedBoard.IsOpen)
                _selectedBoard.Write(e.KeyChar.ToString());

        }
    }
}
