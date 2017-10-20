﻿/*	
	Gamma Analyzer - Controlling application for Burn
    Copyright (C) 2016  Norwegian Radiation Protection Authority

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
// Authors: Dag Robole,

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Threading;
using System.Globalization;
using System.Windows.Forms;
using log4net;
using log4net.Core;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace crash
{
    public partial class FormContainer : Form
    {
        public ILog log = null;

        // Structure with application settings stored on disk
        GASettings settings = new GASettings();        

        // External forms
        FormMain formMain = null;
        FormLog formLog = null;
        FormWaterfallLive formWaterfallLive = null;
        FormROILive formROILive = null;
        FormMap formMap = null;

        public FormContainer()
        {
            InitializeComponent();

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        private void FormContainer_Load(object sender, EventArgs e)
        {            
            formLog = new FormLog(this);
            IntPtr iptr = formLog.Handle; // Force window handle creation
            log = GetLog(formLog.GetTextBox());            

            // Load settings
            LoadSettings();

            formWaterfallLive = new FormWaterfallLive(this, settings.ROIList);

            formROILive = new FormROILive(this, settings.ROIList);

            formMap = new FormMap(this);

            formMain = new FormMain(this, settings, formMap, formWaterfallLive, formROILive);

            menuItemLayoutSession_Click(sender, e);

            // Set up custom events
            formWaterfallLive.SetSessionIndexEvent += formMain.SetSessionIndexEvent;
            formROILive.SetSessionIndexEvent += formMain.SetSessionIndexEvent;
            formMap.SetSessionIndexEvent += formMain.SetSessionIndexEvent;

            statusLabel.Text = "";
        }

        private ILog GetLog(RichTextBox tb)
        {
            if (log != null)
                throw new Exception("Log already initalized");
                
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            PatternLayout patternLayout = new PatternLayout();
            patternLayout.ConversionPattern = "%date [%thread] - %message%newline";
            patternLayout.ActivateOptions();

            RollingFileAppender roller = new RollingFileAppender();
            roller.AppendToFile = false;
            roller.File = GAEnvironment.SettingsPath + Path.DirectorySeparatorChar + "gamma-analyzer.log";
            roller.Layout = patternLayout;
            roller.MaxSizeRollBackups = 3;
            roller.MaximumFileSize = "10MB";
            roller.RollingStyle = RollingFileAppender.RollingMode.Size;
            roller.StaticLogFileName = true;
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            TextBoxAppender textBoxAppender = new TextBoxAppender(tb);
            textBoxAppender.Threshold = Level.All;
            textBoxAppender.Layout = patternLayout;
            textBoxAppender.ActivateOptions();
            hierarchy.Root.AddAppender(textBoxAppender);

            hierarchy.Root.Level = Level.All;
            hierarchy.Configured = true;

            return LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        public void SaveSettings()
        {
            // Serialize settings to file
            using (StreamWriter sw = new StreamWriter(GAEnvironment.SettingsFile))
            {
                XmlSerializer x = new XmlSerializer(settings.GetType());
                x.Serialize(sw, settings);
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(GAEnvironment.SettingsFile))
                SaveSettings();

            // Deserialize settings from file
            using (StreamReader sr = new StreamReader(GAEnvironment.SettingsFile))
            {
                XmlSerializer x = new XmlSerializer(settings.GetType());
                settings = x.Deserialize(sr) as GASettings;
            }
        }

        private void menuItemFileExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void menuItemViewToolbar_Click(object sender, EventArgs e)
        {
            tools.Visible = menuItemViewToolbar.Checked;
        }

        private void menuItemViewStatus_Click(object sender, EventArgs e)
        {
            status.Visible = menuItemViewStatus.Checked;
        }

        private void menuItemHelpAbout_Click(object sender, EventArgs e)
        {
            About about = new About();
            about.ShowDialog();
        }

        private void menuItemLayoutCascade_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.Cascade);
        }

        private void menuItemLayoutTileVertical_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileVertical);
        }

        private void menuItemLayoutTileHorizontal_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileHorizontal);
        }

        private void menuItemLayoutArrangeIcons_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.ArrangeIcons);
        }

        public void menuItemLayoutSetup_Click(object sender, EventArgs e)
        {
            formWaterfallLive.Hide();
            formROILive.Hide();
            formMap.Hide();
            formMain.Show();
            formMain.WindowState = FormWindowState.Normal;
            formLog.Show();
            formLog.WindowState = FormWindowState.Normal;

            Rectangle rect = ClientRectangle;
            Size size = ClientSize;

            int height = size.Height - menu.Height - 4;
            if (tools.Visible)
                height -= tools.Height;
            if (status.Visible)
                height -= status.Height;

            int mainHeight = (int)((double)height * 0.8);
            int logHeight = (int)((double)height * 0.2);

            formMain.Left = rect.Left;
            formMain.Width = size.Width - 4;
            formMain.Top = rect.Top;
            formMain.Height = mainHeight;

            formLog.Left = rect.Left;
            formLog.Width = size.Width - 4;
            formLog.Top = mainHeight;
            formLog.Height = logHeight;
        }

        public void menuItemLayoutSession_Click(object sender, EventArgs e)
        {
            menuItemWindowShowAll_Click(sender, e);

            Rectangle rect = ClientRectangle;
            Size size = ClientSize;

            int thirdWidth = (size.Width - 4) / 3;
            int height = size.Height - menu.Height - 4;
            if (tools.Visible)
                height -= tools.Height;
            if(status.Visible)
                height -= status.Height;
            int thirdHeight = height / 3;

            formMain.Left = rect.Left;
            formMain.Width = rect.Left + thirdWidth * 2;
            formMain.Top = rect.Top;
            formMain.Height = rect.Top + thirdHeight * 2;

            formWaterfallLive.Left = rect.Left;
            formWaterfallLive.Width = rect.Left + thirdWidth * 2;
            formWaterfallLive.Top = rect.Top + thirdHeight * 2;
            formWaterfallLive.Height = thirdHeight;

            formMap.Left = formMain.Left + formMain.Width;
            formMap.Width = thirdWidth;
            formMap.Top = rect.Top;
            formMap.Height = thirdHeight + thirdHeight / 2;

            formROILive.Left = formMain.Left + formMain.Width;
            formROILive.Width = thirdWidth;
            formROILive.Top = formMap.Top + formMap.Height;
            formROILive.Height = thirdHeight;

            formLog.Left = formMain.Left + formMain.Width;
            formLog.Width = thirdWidth;
            formLog.Top = formROILive.Top + formROILive.Height;
            formLog.Height = thirdHeight - thirdHeight / 2;
        }

        private void menuItemWindowROITable_Click(object sender, EventArgs e)
        {
            FormROITable form = new FormROITable(settings.ROIList);
            form.ShowDialog();
        }

        private void menuItemWindowShowAll_Click(object sender, EventArgs e)
        {
            formLog.Show();
            formLog.WindowState = FormWindowState.Normal;
            formROILive.Show();
            formROILive.WindowState = FormWindowState.Normal;
            formROILive.UpdatePane();
            formMap.Show();
            formMap.WindowState = FormWindowState.Normal;
            formWaterfallLive.Show();
            formWaterfallLive.WindowState = FormWindowState.Normal;
            formWaterfallLive.UpdatePane();
            formMain.Show();
            formMain.WindowState = FormWindowState.Normal;
        }        

        private void FormContainer_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();

            formMain.Shutdown();
            formMain.Close();
            formWaterfallLive.Close();
            formROILive.Close();
            formMap.Close();
            formLog.Close();
        }
    }

    public class TextBoxAppender : AppenderSkeleton
    {
        private RichTextBox mTextBox;

        public TextBoxAppender(RichTextBox tb)
        {
            mTextBox = tb;
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            mTextBox.BeginInvoke((MethodInvoker)delegate
            {
                mTextBox.Text = RenderLoggingEvent(loggingEvent) + mTextBox.Text;
            });
        }
    }
}
