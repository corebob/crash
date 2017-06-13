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
// Authors: Dag Robole,

using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Drawing;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Windows.Forms;
using ZedGraph;
using Newtonsoft.Json;

namespace crash
{
    public partial class FormMain
    {
        // Structure with application settings stored on disk
        CrashSettings settings = new CrashSettings();
        
        // Concurrent queue used to pass messages to networking thread
        static ConcurrentQueue<Dictionary<string, object>> sendq = null;
        // Concurrent queue used to receive messages from network thread
        static ConcurrentQueue<Dictionary<string, object>> recvq = null;
        // FIXME: Create a proper API for communication with network thread

        // Networking thread
        static burn.NetService netService = new burn.NetService(ref sendq, ref recvq);
        static Thread netThread = new Thread(netService.DoWork);

        // Timer used to poll for network messages
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();        

        // Structire for currently loaded session
        Session session = new Session();
        
        // External forms
        FormWaterfallLive formWaterfallLive = null;
        FormROILive formROILive = null;
        FormMap frmMap = null;

        // Point lists with graph data
        PointPairList setupGraphList = new PointPairList();
        PointPairList sessionGraphList = new PointPairList();
        PointPairList bkgGraphList = new PointPairList();        

        // Livetime scale factor for currently loaded background
        float bkgScale = 1f;

        // Flag used for tracking multi selection of spectrums
        bool selectionRun = false;        

        // Running session state
        bool sessionRunning = false;

        // Structure containing loaded nuclide library
        List<NuclideInfo> NuclideLibrary = new List<NuclideInfo>();

        bool previewSession = false;
        // Spectrum used to accumulate counts for setup UI
        Spectrum previewSpec = null;

        // Array containing currently selected energies/channels
        List<ChannelEnergy> energyLines = new List<ChannelEnergy>();

        // Array containing curve fitting coefficients
        List<double> coefficients = new List<double>();

        // Enumeration used to keep track of graph object types
        public enum GraphObjectType
        {
            Spectrum,
            Background,
            Energy,
            EnergyTolerance,
            EnergyCalibration
        };

        public void makeGraphObjectType(ref object tag, GraphObjectType got)
        {
            // Create a graph object type
            tag = new GraphObjectType();
            tag = got;
        }

        public bool isGraphObjectType(object tag, GraphObjectType got)
        {
            // Check graph object type
            return tag != null && (GraphObjectType)tag == got;
        }

        private void SaveSettings()
        {
            // Serialize settings to file
            using (StreamWriter sw = new StreamWriter(CrashEnvironment.SettingsFile))
            {
                XmlSerializer x = new XmlSerializer(settings.GetType());
                x.Serialize(sw, settings);
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(CrashEnvironment.SettingsFile))
                return;

            // Deserialize settings from file
            using (StreamReader sr = new StreamReader(CrashEnvironment.SettingsFile))
            {
                XmlSerializer x = new XmlSerializer(settings.GetType());
                settings = x.Deserialize(sr) as CrashSettings;
            }
        }

        private bool LoadNuclideLibrary()
        {
            if (!File.Exists(CrashEnvironment.NuclideLibraryFile))
                return false;

            // Load nuclide library from file
            using (TextReader reader = File.OpenText(CrashEnvironment.NuclideLibraryFile))
            {
                NuclideLibrary.Clear();
                char[] itemDelims = new char[] { ' ', '\t' };
                char[] energyDelims = new char[] { ':' };
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (String.IsNullOrEmpty(line) || line.StartsWith("#")) // Skip comments
                        continue;
                    string[] items = line.Split(itemDelims, StringSplitOptions.RemoveEmptyEntries);
                    string name = items[0];
                    double halfLife = Convert.ToDouble(items[1], CultureInfo.InvariantCulture);
                    string halfLifeUnit = items[2];

                    // Parse nuclide
                    NuclideInfo ni = new NuclideInfo(name, halfLife, halfLifeUnit);

                    // Parse energies
                    for (int i = 3; i < items.Length; i++)
                    {
                        string[] energy = items[i].Split(energyDelims, StringSplitOptions.RemoveEmptyEntries);
                        double e = Convert.ToDouble(energy[0], CultureInfo.InvariantCulture);
                        double p = Convert.ToDouble(energy[1], CultureInfo.InvariantCulture);
                        ni.Energies.Add(new NuclideEnergy(e, p));
                    }

                    // Store nuclide
                    NuclideLibrary.Add(ni);
                }             
            }
            return true;
        }

        private void sendMsg(Dictionary<string, object> msg)
        {
            // Put a message on the network queue
            sendq.Enqueue(msg);
        }

        private bool dispatchRecvMsg(Dictionary<string, object> msg)
        {
            // Handle messages received from network
            Detector det = null;
            DetectorType detType = null;

            switch (msg["command"].ToString())
            {
                case "start_session_success":
                    // New session created successfully                    
                    if (previewSession)
                    {
                        // New session is a preview session, update log
                        Utils.Log.Add("Session started: " + msg["session_name"]);
                    }
                    else
                    {
                        // New session is a normal session
                        string sessionName = msg["session_name"].ToString();
                        Utils.Log.Add("Session started: " + msg["session_name"]);

                        // Create a session object and update state
                        float livetime = Convert.ToSingle(msg["livetime"]);                        

                        det = (Detector)cboxSetupDetector.SelectedItem;
                        detType = settings.DetectorTypes.Find(dt => dt.Name == det.TypeName);
                        session = new Session(settings.SessionRootDirectory, sessionName, "", livetime, det, detType);

                        // Create session files and directories
                        SaveSession(session);

                        // Notify external forms about new session
                        formWaterfallLive.SetSession(session);
                        formROILive.SetSession(session);
                        frmMap.SetSession(session);

                        sessionRunning = true;
                    }
                    btnSetupStartTest.Enabled = false;
                    btnSetupStopTest.Enabled = true;
                    break;

                case "start_session_error":
                    // Creation of new session failed, log error message
                    Utils.Log.Add(msg["message"].ToString());
                    break;

                case "stop_session_success":
                    // Stop session successful, update state
                    Utils.Log.Add("Session stopped");
                    sessionRunning = false;
                    btnSetupStartTest.Enabled = true;
                    btnSetupStopTest.Enabled = false;
                    break;

                case "stop_session_error":
                    // Session stopped successfully
                    Utils.Log.Add(msg["message"].ToString());
                    sessionRunning = false;
                    break;

                case "error":
                    // An error occurred, log error message
                    Utils.Log.Add(msg["message"].ToString());
                    break;                

                case "error_socket":
                    // An socket error occurred, log error message
                    Utils.Log.Add("Socket error: " + msg["message"]);
                    break;

                case "detector_config_success":
                    // Set gain command executed successfully
                    Utils.Log.Add("detector_config_success: " + msg["detector_type"] + " " + msg["voltage"] + " " 
                        + msg["coarse_gain"] + " " + msg["fine_gain"] + " " + msg["num_channels"] + " " 
                        + msg["lld"] + " " + msg["uld"]);

                    // Update selected detector parameters
                    det = (Detector)cboxSetupDetector.SelectedItem;
                    det.CurrentHV = Convert.ToInt32(msg["voltage"]);
                    det.CurrentCoarseGain = Convert.ToDouble(msg["coarse_gain"]);
                    det.CurrentFineGain = Convert.ToDouble(msg["fine_gain"]);
                    det.CurrentNumChannels = Convert.ToInt32(msg["num_channels"]);
                    det.CurrentLLD = Convert.ToInt32(msg["lld"]);
                    det.CurrentULD = Convert.ToInt32(msg["uld"]);
                    
                    // Update state
                    btnSetupNext.Enabled = true;
                    panelSetupGraph.Enabled = true;
                    break;

                case "spectrum":
                    // Session spectrum received successfully
                    Spectrum spec = new Spectrum(msg);
                    spec.CalculateDoserate(session.Detector, session.GEFactor);

                    if (previewSession)
                    {
                        // Spectrum is a preview spectrum
                        Utils.Log.Add(spec.Label + " received");

                        // Merge spectrum with our preview spectrum
                        if (previewSpec == null)
                            previewSpec = spec;
                        else previewSpec.Merge(spec);

                        // Reset and prepare setup graph
                        GraphPane pane = graphSetup.GraphPane;
                        pane.Chart.Fill = new Fill(SystemColors.ButtonFace);
                        pane.Fill = new Fill(SystemColors.ButtonFace);

                        pane.Title.Text = "Setup";
                        pane.XAxis.Title.Text = "Channel";
                        pane.YAxis.Title.Text = "Counts";

                        // Update setup graph
                        setupGraphList.Clear();
                        for (int i = 0; i < previewSpec.Channels.Count; i++)
                            setupGraphList.Add((double)i, (double)previewSpec.Channels[i]);

                        pane.XAxis.Scale.Min = 0;
                        pane.XAxis.Scale.Max = previewSpec.MaxCount;

                        pane.YAxis.Scale.Min = 0;
                        pane.YAxis.Scale.Max = previewSpec.MaxCount + (previewSpec.MaxCount / 10.0);

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
                        // Normal session spectrum received successfully
                        Utils.Log.Add(spec.Label + " received");

                        // FIXME: Make sure session is allocated in case spectrums are ticking in

                        // Store spectrum to disk
                        string sessionPath = settings.SessionRootDirectory + Path.DirectorySeparatorChar + session.Name;
                        string jsonPath = sessionPath + Path.DirectorySeparatorChar + "json";
                        if (!Directory.Exists(jsonPath))
                            Directory.CreateDirectory(jsonPath);

                        string json = JsonConvert.SerializeObject(msg, Newtonsoft.Json.Formatting.Indented);
                        using (TextWriter writer = new StreamWriter(jsonPath + Path.DirectorySeparatorChar + spec.SessionIndex + ".json"))
                        {
                            writer.Write(json);
                        }

                        // Add spectrum to session
                        session.Add(spec);

                        // Add spectrum to UI list
                        bool updateSelectedIndex = false;
                        if (lbSession.SelectedIndex == 0)
                            updateSelectedIndex = true;

                        lbSession.Items.Insert(0, spec);

                        if (updateSelectedIndex)
                        {
                            lbSession.ClearSelected();
                            lbSession.SetSelected(0, true);
                        }

                        // Notify external forms about new spectrum
                        frmMap.AddMarker(spec);
                        formWaterfallLive.UpdatePane();
                        formROILive.UpdatePane();
                    }
                    break;

                default:
                    // Unhandled message received, update log
                    Utils.Log.Add("Unknown message: " + msg["command"].ToString()); // FIXME
                    break;
            }

            return true;
        }        

        public void SaveSession(Session s)
        {
            // Save session info to disk
            string sessionSettingsFile = settings.SessionRootDirectory + Path.DirectorySeparatorChar + s.Name + Path.DirectorySeparatorChar + "session.json";
            string jSessionInfo = JsonConvert.SerializeObject(s, Newtonsoft.Json.Formatting.Indented);            
            using (TextWriter writer = new StreamWriter(sessionSettingsFile))            
                writer.Write(jSessionInfo);
        }

        void SetSessionIndexEvent(object sender, SetSessionIndexEventArgs e)
        {
            // An external form has changed the spectrum selection, update UI
            if (e.StartIndex == -1 && e.EndIndex == -1)
            {
                lbSession.ClearSelected();
                return;
            }

            if (e.StartIndex >= lbSession.Items.Count || e.EndIndex >= lbSession.Items.Count || e.StartIndex < 0 || e.EndIndex < 0)
                return;

            lbSession.ClearSelected();

            if (e.StartIndex < e.EndIndex) // Bizarre, but true
            {
                int tmp = e.StartIndex;
                e.StartIndex = e.EndIndex;
                e.EndIndex = tmp;
            }

            if (e.StartIndex == e.EndIndex)
            {
                int idx1 = lbSession.FindStringExact(session.Name + " - " + e.StartIndex.ToString());
                if (idx1 != ListBox.NoMatches)
                    lbSession.SetSelected(idx1, true);
            }
            else
            {
                int idx1 = lbSession.FindStringExact(session.Name + " - " + e.StartIndex.ToString());
                int idx2 = lbSession.FindStringExact(session.Name + " - " + e.EndIndex.ToString());
                for (int i = idx1; i < idx2; i++)
                {
                    if (i == idx2 - 1)
                        selectionRun = false;

                    lbSession.SetSelected(i, true);

                    if (i == idx1)
                        selectionRun = true;
                }
            }
        }

        private void ClearSpectrumInfo()
        {
            // Clear info about currently selected spectrum
            lblSession.Text = "";
            lblRealtime.Text = "";
            lblLivetime.Text = "";
            lblIndex.Text = "";
            lblLatitudeStart.Text = "";
            lblLongitudeStart.Text = "";
            lblAltitudeStart.Text = "";
            lblLatitudeEnd.Text = "";
            lblLongitudeEnd.Text = "";
            lblAltitudeEnd.Text = "";
            lblGpsTimeStart.Text = "";
            lblGpsTimeEnd.Text = "";
            lblMaxCount.Text = "";
            lblMinCount.Text = "";
            lblTotalCount.Text = "";
            lblDoserate.Text = "";
            lblSessionChannel.Text = "";
            lblSessionEnergy.Text = "";
        }

        private void ClearSetup()
        {   
            // Clear info about current setup
            graphSetup.GraphPane.CurveList.Clear();
            graphSetup.GraphPane.GraphObjList.Clear();            
            graphSetup.Invalidate();
        }

        private void ClearSession()
        {
            // Clear currently loaded session
            lbSession.Items.Clear();
            lblSessionDetector.Text = "";
            graphSession.GraphPane.CurveList.Clear();
            graphSession.GraphPane.GraphObjList.Clear();
            graphSession.Invalidate();
            ClearSpectrumInfo();            

            // Notify external forms about clearing session
            formWaterfallLive.ClearSession();
            formROILive.ClearSession();
            frmMap.ClearSession();

            session.Clear();
        }

        private void ClearBackground()
        {            
            if (session == null)
                return;

            // Clear currently selected background and update state
            session.SetBackground(null);

            lblBackground.Text = "";
            graphSession.Invalidate();
        }

        public void ShowSpectrum(string title, float[] channels, float maxCount, float minCount)
        {
            if (session == null)
                return;

            // Reset spectrum graphpane
            GraphPane pane = graphSession.GraphPane;
            pane.Chart.Fill = new Fill(SystemColors.ButtonFace);
            pane.Fill = new Fill(SystemColors.ButtonFace);

            pane.Title.Text = title;
            pane.XAxis.Title.Text = "Channel";
            pane.YAxis.Title.Text = "Counts";

            pane.XAxis.Scale.Min = 0;
            pane.XAxis.Scale.Max = maxCount;

            pane.YAxis.Scale.Min = minCount;
            pane.YAxis.Scale.Max = maxCount + (maxCount / 10.0);

            pane.CurveList.Clear();

            // Update background graph
            if (session.Background != null)
            {
                bkgGraphList.Clear();
                for (int i = 0; i < session.Background.Length; i++)
                    bkgGraphList.Add((double)i, (double)session.Background[i] * bkgScale);

                LineItem bkgCurve = pane.AddCurve("Background", bkgGraphList, Color.Blue, SymbolType.None);
                bkgCurve.Line.IsSmooth = true;
                bkgCurve.Line.SmoothTension = 0.5f;
            }

            // Update spectrum graph
            sessionGraphList.Clear();
            for (int i = 0; i < channels.Length; i++)
                sessionGraphList.Add((double)i, (double)channels[i]);

            LineItem curve = pane.AddCurve("Spectrum", sessionGraphList, Color.Red, SymbolType.None);
            curve.Line.IsSmooth = true;
            curve.Line.SmoothTension = 0.5f;

            // Update state
            pane.Chart.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
            pane.Legend.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
            pane.Fill = new Fill(SystemColors.ButtonFace, SystemColors.ButtonFace);
        }        

        private void GetGraphPointFromMousePos(int posX, int posY, ZedGraphControl graph, out int x, out int y)
        {
            // Convert mouse position to graph position
            int index = 0;
            object nearestobject = null;
            PointF clickedPoint = new PointF(posX, posY);
            graph.GraphPane.FindNearestObject(clickedPoint, this.CreateGraphics(), out nearestobject, out index);
            double dx, dy;
            graph.GraphPane.ReverseTransform(clickedPoint, out dx, out dy);
            x = (int)dx;
            y = (int)dy;
        }        

        private void PopulateDetectorTypeList()
        {
            // Update preferences detector type UI
            lvDetectorTypes.Items.Clear();
            foreach (DetectorType dt in settings.DetectorTypes)
            {
                ListViewItem item = new ListViewItem(
                    new string[] { 
                        dt.Name, 
                        dt.MaxNumChannels.ToString(), 
                        dt.MinHV.ToString(), 
                        dt.MaxHV.ToString(),
                        dt.GEScript
                });
                item.Tag = dt;
                lvDetectorTypes.Items.Add(item);
            }
        }

        private void PopulateDetectorList()
        {
            // Update preferences detector UI
            lvDetectors.Items.Clear();
            foreach (Detector d in settings.Detectors)
            {
                ListViewItem item = new ListViewItem(new string[] {                     
                    d.Serialnumber, 
                    d.TypeName, 
                    d.CurrentNumChannels.ToString(), 
                    d.CurrentHV.ToString(), 
                    d.CurrentCoarseGain.ToString(), 
                    d.CurrentFineGain.ToString(),                    
                    d.CurrentLivetime.ToString(),
                    d.CurrentLLD.ToString(),
                    d.CurrentULD.ToString()
                });
                item.Tag = d;
                lvDetectors.Items.Add(item);
            }
        }

        private void PopulateDetectors()
        {
            // Update setup detector UI
            cboxSetupDetector.Items.Clear();
            foreach (Detector d in settings.Detectors)
                cboxSetupDetector.Items.Add(d);
        }

        int GetChannelFromEnergy(Detector det, double E, int startX, int endX)
        {
            // Locate a suitable channel for a given energy, return -1 if none is found

            // FIXME: O(log n) ?
            double epsilon = 2d;
            for (int x = startX; x < endX; x++)
            {
                double e = det.GetEnergy(x);
                if (Math.Abs(E - e) < epsilon)
                    return x;
            }
            return -1;
        }
    }
}
