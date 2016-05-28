using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;
using System.Diagnostics;

namespace LineExtractTest
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            hardwareInfoLabel.Text = string.Format("IsHardwareAccelerated: {0}", Vector.IsHardwareAccelerated.ToString());
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            Bitmap src = null;
            Bitmap dest = null;
            Bitmap orig = null;
            try
            {
                orig = new Bitmap(openFileDialog1.FileName);
                src = new Bitmap(orig.Width, orig.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                using (Graphics gr = Graphics.FromImage(src))
                {
                    gr.DrawImage(orig, new Rectangle(0, 0, src.Width, src.Height));
                }
                
                dest = new Bitmap(src.Width, src.Height);

                Stopwatch sw = new Stopwatch();
                sw.Start();
                LineExtractProcessor p = new LineExtractProcessor(src);
                p.Threshold = (int)threshold.Value;
                p. ExtractLineArt(dest);
                sw.Stop();

                processingTimeLabel.Text = string.Format("Processing Time: {0:N0}msec", sw.ElapsedMilliseconds);

                mainPictureBox.Width = dest.Width;
                mainPictureBox.Height = dest.Height;
                if (mainPictureBox.Image != null) { mainPictureBox.Image.Dispose(); }
                mainPictureBox.Image = new Bitmap(dest);


            }
            finally
            {
                if (orig != null) { src.Dispose(); }
                if (src != null) { src.Dispose(); }
                if (dest != null) { dest.Dispose(); }
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                mainPictureBox.Image.Save(saveFileDialog1.FileName, System.Drawing.Imaging.ImageFormat.Png);
            }

        }
    }
}
