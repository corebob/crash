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
using NLua;
using Newtonsoft.Json;

namespace crash
{
    // Class to store a session
    public class Session
    {
        // Global Lua object
        private static Lua LuaEngine = new Lua();

        // Function used to calculate GE factor
        public LuaFunction GEScriptFunc = null;

        // IP Address of peer  
        public string IPAddress { get; set; }        

        // Name of session
        public string Name { get; set; }

        // Session database file
        public string SessionFile { get; set; }

        // Comment for this session
        public string Comment { get; set; }

        // Livetime used for this session
        public float Livetime { get; set; }

        // Detector definition used with this session
        private Detector mDetector;

        public Detector Detector
        {
            get { return mDetector; }
            set { mDetector = value; LoadGEScriptFunc(); }
        }

        // Number of channels used with this session
        public float NumChannels { get; private set; }

        // Max number of channel counts found for this session
        public float MaxChannelCount { get; private set; }

        // Min number of channel counts found for this session
        public float MinChannelCount { get; private set; }

        // List of spectrums stored in this session
        public List<Spectrum> Spectrums { get; private set; }

        // Background counts to use with this session
        public float[] Background = null;

        // Loaded state for this session
        public bool IsLoaded { get { return !String.IsNullOrEmpty(Name); } }

        // Empty state for this session
        public bool IsEmpty { get { return Spectrums.Count == 0; } }

        public Session()
        {
            Spectrums = new List<Spectrum>();
            Clear();            
        }

        public Session(string ip, string sessionFile, string name, string comment, float livetime, Detector det)
        {
            Spectrums = new List<Spectrum>();
            Clear();

            IPAddress = ip;
            Name = name;
            SessionFile = sessionFile;
            Comment = comment;
            Livetime = livetime;
            Detector = det;
        }

        public bool Add(Spectrum spec)
        {
            // Add a new spectrum to the list of spectrums            
            int idx = Array.FindLastIndex(Spectrums.ToArray(), x => x.SessionIndex < spec.SessionIndex);
            Spectrums.Insert(idx + 1, spec);
            
            NumChannels = spec.NumChannels;

            // Update state
            if (spec.MaxCount > MaxChannelCount)
                MaxChannelCount = spec.MaxCount;
            if (spec.MinCount < MinChannelCount)
                MinChannelCount = spec.MinCount;

            return true;
        }        

        public void Clear()
        {
            // Clear this session

            Name = String.Empty;
            SessionFile = String.Empty;
            Comment = String.Empty;
            Livetime = 0;            
            mDetector = null;
            NumChannels = 0;
            MaxChannelCount = 0;
            MinChannelCount = 0;            
            Spectrums.Clear();
            GEScriptFunc = null;           
            Background = null;
        }

        private bool LoadGEScriptFunc()
        {
            // Initialize GE factor function if GE script exists

            string geScriptFile = GAEnvironment.GEScriptPath + Path.DirectorySeparatorChar + mDetector.GEScript;

            if (!File.Exists(geScriptFile))            
                throw new Exception("GE script does not exist: " + geScriptFile);            

            GEScriptFunc = null;            
            string script = File.ReadAllText(geScriptFile);
            LuaEngine.DoString(script);
            GEScriptFunc = LuaEngine["gevalue"] as LuaFunction;
            
            return GEScriptFunc != null;
        }

        public bool SetBackground(List<Spectrum> specs)
        {
            // Set background counts for this session and adjust for livetime

            if (specs.Count < 1)
            {
                Background = null;
                return true;
            }

            Background = GetAdjustedCounts(specs, Livetime);

            return true;
        }

        public bool SetBackgroundSession(Session bkg)
        {
            // Set background counts for this session and adjust for livetime

            if (bkg == null)
            {
                Background = null;
                return true;
            }                

            if (IsEmpty || !IsLoaded || bkg.IsEmpty || !bkg.IsLoaded)
                return false;
            
            Background = bkg.GetAdjustedCounts(Livetime);

            return true;
        }

        private float[] GetAdjustedCounts(List<Spectrum> specs, float targetLivetime)
        {
            // Adjust counts for a given livetime

            if (specs.Count < 1)
                return null;

            float[] spec = new float[(int)NumChannels];

            foreach (Spectrum s in specs)
                for (int i = 0; i < s.Channels.Count; i++)
                    spec[i] += s.Channels[i];

            float scale = targetLivetime / Livetime;

            for (int i = 0; i < spec.Length; i++)
            {
                spec[i] /= (float)specs.Count;
                spec[i] *= scale;
            }

            return spec;
        }

        private float[] GetAdjustedCounts(float targetLivetime)
        {
            return GetAdjustedCounts(Spectrums, targetLivetime);
        }

        public float GetCountInBkg(int start, int end)
        {
            // Accumulate counts for a given region

            if (Background == null)
                return 0f;

            float cnt = 0f;

            for (int i = start; i < end; i++)            
                cnt += Background[i];                
            
            return cnt;
        }

        public float GetMaxCountInROI(int start, int end)
        {
            // Find highest count for a given region

            float max = -1f;

            foreach (Spectrum s in Spectrums)
            {
                float curr = s.GetCountInROI(start, end);
                if (max == -1f)
                    max = curr;
                else if(curr > max)
                    max = curr;
            }
            return max;
        }

        public double GetBackgroundCountInROI(int start, int end)
        {
            // Find highest count for a given region in background

            if (Background == null)
                return 0d;

            double cnt = 0d;

            for (int i=start; i<end; i++)            
                cnt += Background[i];
            
            return cnt;
        }
    }

    public class APISession
    {
        public APISession(Session s)
        {
            Name = s.Name;
            IPAddress = s.IPAddress;
            Comment = s.Comment;
            Livetime = s.Livetime;
            DetectorData = JsonConvert.SerializeObject(s.Detector, Newtonsoft.Json.Formatting.None);
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("ip_address")]
        public string IPAddress { get; set; }

        [JsonProperty("comment")]
        public string Comment { get; set; }

        [JsonProperty("livetime")]
        public double Livetime { get; set; }

        [JsonProperty("detector_data")]
        public string DetectorData { get; set; }
    }
}
