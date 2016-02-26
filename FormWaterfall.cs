﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace crash
{
    public partial class FormWaterfallLive : Form
    {
        Session session = null;        
        Bitmap bmp = null;        

        public FormWaterfallLive()
        {
            InitializeComponent();
            DoubleBuffered = true;
        }        

        private void FormWaterfall_Load(object sender, EventArgs e)
        {                        
            pane_Resize(sender, e);
            UpdateStats();
        }

        private void UpdateStats()
        {
            lblColorCeil.Text = "Color ceiling (min, curr, max): " + tbColorCeil.Minimum + ", " + tbColorCeil.Value + ", " + tbColorCeil.Maximum;
        }

        public void SetSession(Session sess)
        {
            session = sess;
        }

        public void Repaint()
        {
            if (WindowState == FormWindowState.Minimized)
                return;

            tbColorCeil.Maximum = (int)session.MaxChannelCount;
            tbColorCeil.Minimum = (int)session.MinChannelCount;

            UpdateStats();

            if (session == null || bmp == null)
                return;

            if (tbColorCeil.Value < 1)
                return;

            float max = tbColorCeil.Value;
            float sectorSize = max / 4f;            
            float scale = 255f / sectorSize;
            int y = 0;

            int h = pane.Height > session.Spectrums.Count ? session.Spectrums.Count : pane.Height - 1;

            for (int i = h - 1; i >= 0; i--)
            {
                Spectrum s = session.Spectrums[i];

                int w = s.Channels.Count > pane.Width ? pane.Width : s.Channels.Count; // FIXME                                

                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 255;
                    float cps = s.Channels[x];
                    int sectorSkip = CalcSectorSkip(cps, sectorSize);

                    float adj = (cps - (float)sectorSkip * sectorSize) * scale;
                    if (adj < 0)
                        adj = 0;
                    if (adj > 255)
                        adj = 255;

                    if (sectorSkip == 0)
                    {
                        g += (int)adj;
                    }
                    else if (sectorSkip == 1)
                    {
                        g = 255;
                        b -= (int)adj;
                    }
                    else if (sectorSkip == 2)
                    {
                        g = 255;
                        b = 0;
                        r += (int)adj;
                    }
                    else
                    {
                        g = 255;
                        b = 0;
                        r = 255;
                        g -= (int)adj;
                    }
                    
                    if (x >= 0 && x < pane.Width && y >= 0 && y < pane.Height)
                        bmp.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
                y++;
            }            

            pane.Refresh();
        }

        private void FormWaterfall_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void pane_Paint(object sender, PaintEventArgs e)        
        {
            if (bmp == null || WindowState == FormWindowState.Minimized)
                return;

            e.Graphics.DrawImage(bmp, 0, 0);
        }

        private void pane_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                return;
            if (pane.Width < 1 || pane.Height < 1)
                return;

            bmp = new Bitmap(pane.Width, pane.Height);
        }

        private int CalcSectorSkip(float cps, float sectorSize)
        {
            if (cps < sectorSize)
                return 0;
            else if (cps < sectorSize * 2f)
                return 1;
            else if (cps < sectorSize * 3f)
                return 2;
            else return 3;
        }

        private void tbColorCeil_ValueChanged(object sender, EventArgs e)
        {
            UpdateStats();
        }
    }
}
