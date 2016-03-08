﻿/*	
	Crash - Controlling application for Burn
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
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Data;
using System.Net;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using System.Globalization;
using ZedGraph;

namespace crash
{
    public partial class FormMain : Form
    {
        private static string SettingsPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments) + Path.DirectorySeparatorChar + "crash";
        private static string SettingsFile = SettingsPath + Path.DirectorySeparatorChar + "settings.xml";
        
        Settings settings = new Settings();

        static ConcurrentQueue<burn.Message> sendq = null;
        static ConcurrentQueue<burn.Message> recvq = null;

        static burn.NetService netService = new burn.NetService(ref sendq, ref recvq);
        static Thread netThread = new Thread(netService.DoWork);
        
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        bool connected = false;
        Session session = null;        
        
        FormConnect formConnect = new FormConnect();        
        FormWaterfallLive formWaterfallLive = new FormWaterfallLive();
        FormWaterfallHistory formWaterfallHist = new FormWaterfallHistory();
        FormROITableHistory formROIHistory = new FormROITableHistory();
        FormMap formMap = new FormMap();
        FormLog log = new FormLog();

        PointPairList setupGraphList = new PointPairList();
        PointPairList sessionGraphList = new PointPairList();        

        public FormMain()
        {
            InitializeComponent();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            tabs.HideTabs = true;
            tabs.SelectedTab = pageMenu;

            lblConnectionStatus.ForeColor = Color.Red;
            lblConnectionStatus.Text = "Not connected";

            if (!Directory.Exists(SettingsPath))
                Directory.CreateDirectory(SettingsPath);

            if (File.Exists(SettingsFile))
            {
                LoadSettings();
            }

            netThread.Start();
            while (!netThread.IsAlive);            
                    
            timer.Interval = 10;
            timer.Tick += timer_Tick;
            timer.Start();                                    
        }

        void timer_Tick(object sender, EventArgs e)
        {
            while (!recvq.IsEmpty)
            {
                burn.Message msg;                
                if (recvq.TryDequeue(out msg))
                    dispatchRecvMsg(msg);                                    
            }            
        }        

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (netService.IsRunning())
                btnStopNetService_Click(sender, e);
            timer.Stop();

            SaveSettings();
        }

        private void SaveSettings()
        {
            StreamWriter sw = new StreamWriter(SettingsFile);
            XmlSerializer x = new XmlSerializer(settings.GetType());
            x.Serialize(sw, settings);
            sw.Close();
        }

        private void LoadSettings()
        {
            StreamReader sr = new StreamReader(SettingsFile);
            XmlSerializer x = new XmlSerializer(settings.GetType());
            settings = x.Deserialize(sr) as Settings;
            sr.Close();
        }        

        private bool dispatchRecvMsg(burn.Message msg)
        {            
            switch (msg.Command)
            {
                case "connect_ok":
                    lblConnectionStatus.ForeColor = Color.Green;
                    lblConnectionStatus.Text = "Connected to " + msg.Arguments["host"] + ":" + msg.Arguments["port"];
                    log.Add("Connected to " + msg.Arguments["host"] + ":" + msg.Arguments["port"]);
                    connected = true;
                    break;

                case "connect_failed":
                    lblConnectionStatus.ForeColor = Color.Red;
                    lblConnectionStatus.Text = "Connection failed for " + msg.Arguments["host"] + ":" + msg.Arguments["port"] + " " + msg.Arguments["message"];
                    log.Add("Connection failed for " + msg.Arguments["host"] + ":" + msg.Arguments["port"] + " " + msg.Arguments["message"]);
                    connected = false;
                    break;

                case "disconnect_ok":
                    lblConnectionStatus.ForeColor = Color.Red;
                    lblConnectionStatus.Text = "Not connected";
                    log.Add("Disconnected from peer");
                    connected = false;
                    break;

                case "close_ok":
                    netService.RequestStop();
                    netThread.Join();
                    lblConnectionStatus.ForeColor = Color.Red;
                    lblConnectionStatus.Text = "Not connected";
                    log.Add("Disconnected from peer, peer closed");
                    break;

                case "new_session_ok":
                    bool prev = msg.Arguments["preview"] == "1";
                    if(prev)
                        log.Add("Preview received");
                    else
                    {
                        string session_name = msg.Arguments["session_name"];
                        log.Add("New session created: " + session_name);
                        
                        session = new Session(session_name);
                        formWaterfallLive.SetSession(session);
                        formROIHistory.SetSession(session);
                        formMap.SetSession(session);                        
                    }                        
                    break;

                case "new_session_failed":
                    log.Add("New session failed: " + msg.Arguments["message"]);
                    break;

                case "stop_session_ok":
                    log.Add("Session stopped");
                    break;

                case "session_finished":
                    log.Add("Session " + msg.Arguments["session_name"] + " finished");
                    break;

                case "error":
                    log.Add("Error: " + msg.Arguments["message"]);
                    break;

                case "error_socket":
                    log.Add("Socket error: " + msg.Arguments["error_code"] + " " + msg.Arguments["message"]);
                    break;                

                case "set_gain_ok":
                    log.Add("set gain: " + msg.Arguments["voltage"] + " " + msg.Arguments["coarse_gain"] + " " + msg.Arguments["fine_gain"]);
                    break;

                case "spectrum":
                    Spectrum spec = new Spectrum(msg);
                    log.Add(spec.Label + " received");

                    string path;

                    if (spec.IsPreview)
                    {
                        path = SettingsPath;                        

                        GraphPane pane = graphSetup.GraphPane;
                        pane.Chart.Fill = new Fill(SystemColors.ButtonFace);
                        pane.Fill = new Fill(SystemColors.ButtonFace);                        

                        pane.Title.Text = "Setup";
                        pane.XAxis.Title.Text = "Channel";
                        pane.YAxis.Title.Text = "Counts";

                        setupGraphList.Clear();
                        for (int i = 0; i < spec.Channels.Count; i++)
                            setupGraphList.Add((double)i, (double)spec.Channels[i]);

                        pane.XAxis.Scale.Min = 0;
                        pane.XAxis.Scale.Max = spec.MaxCount;

                        pane.YAxis.Scale.Min = 0;
                        pane.YAxis.Scale.Max = spec.MaxCount + (spec.MaxCount / 10.0);

                        pane.CurveList.Clear();

                        LineItem curve = pane.AddCurve("Spectrum", setupGraphList, Color.Red, SymbolType.None);
                        curve.Line.Fill = new Fill(SystemColors.ButtonFace, Color.Red, 45F);                        
                        pane.Chart.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
                        pane.Legend.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);                        
                        pane.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);                        

                        graphSetup.RestoreScale(pane);
                        graphSetup.AxisChange();
                        graphSetup.Refresh();                        
                    }
                    else
                    {                        
                        path = settings.SessionDirectory + Path.DirectorySeparatorChar + spec.SessionName;                        
                        session.Add(spec);

                        // Add list node
                        lbSession.Items.Insert(0, spec);

                        formMap.AddMarker(spec);
                        formWaterfallLive.UpdatePane();
                        formROIHistory.UpdatePane();
                    }                        

                    string jsonPath = path + Path.DirectorySeparatorChar + "json";
                    if (!Directory.Exists(jsonPath))
                        Directory.CreateDirectory(jsonPath);

                    string filename = jsonPath + Path.DirectorySeparatorChar + spec.SessionIndex + ".json";
                    TextWriter writer = new StreamWriter(filename);
                    writer.Write(msg.ToJson(true));
                    writer.Close();

                    if(cbStoreChn.Checked)
                    {
                        string chnPath = path + Path.DirectorySeparatorChar + "chn";
                        if (!Directory.Exists(chnPath))
                            Directory.CreateDirectory(chnPath);
                        filename = chnPath + Path.DirectorySeparatorChar + spec.SessionIndex + ".chn";                        
                        burn.CHN.Write(filename, msg);
                    }
                    break;

                default:
                    string info = msg.Command + " -> ";
                    foreach (KeyValuePair<string, string> item in msg.Arguments)
                        info += item.Key + ":" + item.Value + ", ";
                    log.Add("Unhandeled command: " + info);
                    break;
            }
            return true;
        }        

        private void menuItemExit_Click(object sender, EventArgs e)
        {
            menuItemDisconnect_Click(sender, e);
            Close();
        }

        private void menuItemConnect_Click(object sender, EventArgs e)
        {
            formConnect.IP = settings.LastIP;
            formConnect.Port = settings.LastPort;
            if (formConnect.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            settings.LastIP = formConnect.IP;
            settings.LastPort = formConnect.Port;

            burn.Message msg = new burn.Message("connect", null);
            msg.AddParameter("host", formConnect.IP);
            msg.AddParameter("port", formConnect.Port);            
            sendq.Enqueue(msg);           
        }

        private void menuItemDisconnect_Click(object sender, EventArgs e)
        {        
            if(connected)
                if (MessageBox.Show("Are you sure you want to disconnect?", "Confirmation", MessageBoxButtons.YesNo) == DialogResult.No)
                    return;

            sendq.Enqueue(new burn.Message("disconnect", null));
        }        

        private void btnSendClose_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to close the remote server?", "Confirmation", MessageBoxButtons.YesNo) == DialogResult.No)
                return;

            sendq.Enqueue(new burn.Message("close", null));                        
        }

        private void btnSendSession_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(settings.SessionDirectory))
            {
                MessageBox.Show("You must provide a session directory under preferences");
                return;
            }                        

            if (String.IsNullOrEmpty(tbSpecLivetime.Text))
            {
                MessageBox.Show("You must specify a livetime");
                return;
            }            

            int count = String.IsNullOrEmpty(tbSpecCount.Text) ? -1 : Convert.ToInt32(tbSpecCount.Text);
            float livetime = Convert.ToSingle(tbSpecLivetime.Text);
            float delay = String.IsNullOrEmpty(tbSpecDelay.Text) ? 0 : Convert.ToSingle(tbSpecDelay.Text);

            lbSession.Items.Clear();

            burn.Message msg = new burn.Message("new_session", null);
            msg.AddParameter("session_name", String.Format("{0:ddMMyyyy_HHmmss}", DateTime.Now));
            msg.AddParameter("preview", 0);
            msg.AddParameter("iterations", count);
            msg.AddParameter("livetime", livetime);
            msg.AddParameter("delay", delay);
            sendq.Enqueue(msg);            
        }        

        private void btnStopNetService_Click(object sender, EventArgs e)
        {
            netService.RequestStop();
            netThread.Join();
        }                
        
        private void btnStopSession_Click(object sender, EventArgs e)
        {
            sendq.Enqueue(new burn.Message("stop_session", null));
        }
        
        private void btnMenuSpec_Click(object sender, EventArgs e)
        {
            tabs.SelectedTab = pageSetup;
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            tabs.SelectedTab = pageMenu;
        }        

        private void btnMenuMap_Click(object sender, EventArgs e)
        {
            tabs.SelectedTab = pageSession;
        }        

        private void btnMenuSession_Click(object sender, EventArgs e)
        {
            tabs.SelectedTab = pageSession;
        }
        
        private void btnSetupSetParams_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(tbSetupVoltage.Text))
            {
                MessageBox.Show("You must specify voltage");
                return;
            }

            if (String.IsNullOrEmpty(tbSetupCoarseGain.Text))
            {
                MessageBox.Show("You must specify coarse gain");
                return;
            }

            if (String.IsNullOrEmpty(tbSetupFineGain.Text))
            {
                MessageBox.Show("You must specify fine gain");
                return;
            }

            int voltage = Convert.ToInt32(tbSetupVoltage.Text);
            float coarse = Convert.ToInt32(tbSetupCoarseGain.Text);
            float fine = Convert.ToInt32(tbSetupFineGain.Text);

            burn.Message msg = new burn.Message("set_gain", null);
            msg.AddParameter("voltage", voltage);
            msg.AddParameter("coarse_gain", coarse);
            msg.AddParameter("fine_gain", fine);
            sendq.Enqueue(msg);
        }

        private void btnSetupStart_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(tbSetupLivetime.Text))
            {
                MessageBox.Show("You must specify a livetime");
                return;
            }

            burn.Message msg = new burn.Message("new_session", null);
            msg.AddParameter("session_name", String.Format("{0:ddMMyyyy_HHmmss}", DateTime.Now));
            msg.AddParameter("preview", 1);
            msg.AddParameter("iterations", 1);
            msg.AddParameter("livetime", Convert.ToSingle(tbSetupLivetime.Text));
            msg.AddParameter("delay", 0);
            sendq.Enqueue(msg);
        }

        private void btnSetupStop_Click(object sender, EventArgs e)
        {
            sendq.Enqueue(new burn.Message("stop_session", null));
        }

        private void btnSetupStoreParams_Click(object sender, EventArgs e)
        {
            // Update settings FIXME
            SaveSettings();
        }                

        private void menuItemPreferences_Click(object sender, EventArgs e)
        {
            FormPreferences form = new FormPreferences(settings);
            form.ShowDialog();
        }

        private void btnShowWaterfall_Click(object sender, EventArgs e)
        {
            formWaterfallLive.Show();
            formWaterfallLive.BringToFront();
            formWaterfallLive.UpdatePane();
        }
                
        private void btnMenuBackgrounds_Click(object sender, EventArgs e)
        {
            tabs.SelectedTab = pageBackground;
        }

        private void tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            lblInterface.Text = tabs.SelectedTab.Text;

            if (tabs.SelectedTab == pageMenu)
                btnBack.Enabled = false;
            else btnBack.Enabled = true;

            if (tabs.SelectedTab == pageSession)
            {
                btnShowWaterfallLive.Visible = true;
                btnShowWaterfallHist.Visible = true;
            }
            else
            {
                btnShowWaterfallLive.Visible = false;
                btnShowWaterfallHist.Visible = false;
            }
        }        

        private void btnShowWaterfallHist_Click(object sender, EventArgs e)
        {
            formWaterfallHist.Show();
            formWaterfallHist.BringToFront();
        }                

        private void btnShowROIChart_Click(object sender, EventArgs e)
        {
            formROIHistory.Show();
            formROIHistory.BringToFront();
        }

        private void btnShowMap_Click(object sender, EventArgs e)
        {
            formMap.Show();
            formMap.BringToFront();
        }

        private void btnShowLog_Click(object sender, EventArgs e)
        {
            log.Show();
            log.BringToFront();
        }

        public void ShowSpectrum(Spectrum s)
        {
            GraphPane pane = graphSession.GraphPane;
            pane.Chart.Fill = new Fill(SystemColors.ButtonFace);
            pane.Fill = new Fill(SystemColors.ButtonFace);

            pane.Title.Text = "Setup";
            pane.XAxis.Title.Text = "Channel";
            pane.YAxis.Title.Text = "Counts";

            sessionGraphList.Clear();
            for (int i = 0; i < s.Channels.Count; i++)
                sessionGraphList.Add((double)i, (double)s.Channels[i]);

            pane.XAxis.Scale.Min = 0;
            pane.XAxis.Scale.Max = s.MaxCount;

            pane.YAxis.Scale.Min = 0;
            pane.YAxis.Scale.Max = s.MaxCount + (s.MaxCount / 10.0);

            pane.CurveList.Clear();

            LineItem curve = pane.AddCurve("Spectrum", sessionGraphList, Color.Red, SymbolType.None);
            curve.Line.Fill = new Fill(SystemColors.ButtonFace, Color.Red, 45F);
            pane.Chart.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
            pane.Legend.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
            pane.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);

            graphSession.RestoreScale(pane);
            graphSession.AxisChange();
            graphSession.Refresh();
        }

        private void lbSession_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lbSession.SelectedItems.Count < 1)
                return;

            Spectrum s = lbSession.SelectedItem as Spectrum;
            ShowSpectrum(s);            
        }
    }    

    public class Spectrum
    {                
        private List<float> mChannels;

        public string SessionName { get; private set; }
        public int SessionIndex { get; private set; }
        public string Label { get; private set; }        
        public List<float> Channels { get { return mChannels; } }
        public float NumChannels { get; private set; }
        public float MaxCount { get; private set; }
        public float MinCount { get; private set; }
        public float TotalCount { get; private set; }
        public bool IsPreview { get; private set; }
        public double LatitudeStart { get; private set; }
        public double LongitudeStart { get; private set; }

        public Spectrum(burn.Message msg)
        {            
            SessionName =  msg.Arguments["session_name"];
            SessionIndex = Convert.ToInt32(msg.Arguments["session_index"]);
            Label = "Spectrum " + SessionIndex.ToString();
            NumChannels = Convert.ToInt32(msg.Arguments["num_channels"]);
            IsPreview = msg.Arguments["preview"] == "1";
            LatitudeStart = Convert.ToDouble(msg.Arguments["latitude_start"], CultureInfo.InvariantCulture);
            LongitudeStart = Convert.ToDouble(msg.Arguments["longitude_start"], CultureInfo.InvariantCulture);
            mChannels = new List<float>();
            TotalCount = 0f;
            string[] items = msg.Arguments["channels"].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);                
            foreach (string item in items)
            {
                float ch = Convert.ToSingle(item);
                mChannels.Add(ch);
                
                if (ch > MaxCount)
                    MaxCount = ch;
                if (ch < MinCount)
                    MinCount = ch;

                TotalCount += ch;
            }
        }        

        public float GetCountInROI(int start, int end)
        {
            float max = 0f;
            for(int i=start; i<end; i++)
                max += mChannels[i];            
            return max;
        }

        public override string ToString()
        {
            return SessionName + " - " + SessionIndex.ToString();
        }
    }

    public class Session
    {
        public string Name { get; private set; }
        public float MaxChannelCount { get; private set; }
        public float MinChannelCount { get; private set; }
        public List<Spectrum> Spectrums { get; private set; }        

        public Session(string name)
        {
            Name = name;
            Spectrums = new List<Spectrum>();            
        }   
     
        public void Add(Spectrum spec)
        {
            Spectrums.Add(spec);

            if (spec.MaxCount > MaxChannelCount)
                MaxChannelCount = spec.MaxCount;
            if (spec.MinCount < MinChannelCount)
                MinChannelCount = spec.MinCount;
        }

        public void Clear()
        {
            Spectrums.Clear();
        }        

        public float GetMaxCountInROI(int start, int end)
        {
            float max = 0f;

            foreach(Spectrum s in Spectrums)
            {
                float curr = s.GetCountInROI(start, end);
                if (curr > max)
                    max = curr;
            }
            return max;
        }
    }
}
