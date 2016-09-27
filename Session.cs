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
using System.Collections.Generic;
using Newtonsoft.Json;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace crash
{
    // Class to store a session
    public class Session
    {
        // Global IronPython object. Ignore this when serializing
        [JsonIgnore]
        private static dynamic PyEngine = Python.CreateEngine();

        // Name of session
        public string Name { get; set; }

        // Comment for this session
        public string Comment { get; set; }

        // Livetime used for this session
        public float Livetime { get; set; }

        // Number of spectrums requested for this session, -1 for infinite
        public int Iterations { get; set; }        

        // Detector definition used with this session
        public Detector Detector { get; set; }

        // Detector type definition used with this session
        public DetectorType DetectorType { get; set; }

        // Number of channels used with this session. Ignore this when serializing
        [JsonIgnore]
        public float NumChannels { get; private set; }

        // Max number of channel counts found for this session. Ignore this when serializing
        [JsonIgnore]
        public float MaxChannelCount { get; private set; }

        // Min number of channel counts found for this session. Ignore this when serializing
        [JsonIgnore]
        public float MinChannelCount { get; private set; }

        // Function used to calculate GE factor. Ignore this when serializing
        [JsonIgnore]
        public dynamic GEFactor { get; set; }

        // List of spectrums stored in this session. Ignore this when serializing
        [JsonIgnore]
        public List<Spectrum> Spectrums { get; private set; }

        // Background counts to use with this session. Ignore this when serializing
        [JsonIgnore]
        public float[] Background = null;

        // Loaded state for this session. Ignore this when serializing
        [JsonIgnore]
        public bool IsLoaded { get { return !String.IsNullOrEmpty(Name); } }

        // Empty state for this session. Ignore this when serializing
        [JsonIgnore]
        public bool IsEmpty { get { return Spectrums.Count == 0; } }

        public Session()
        {
            Spectrums = new List<Spectrum>();
            Clear();            
        }

        public Session(string sessionPath, string name, string comment, float livetime, int iterations, Detector det, DetectorType detType)
        {
            Spectrums = new List<Spectrum>();
            Clear();            

            Name = name;
            Comment = comment;
            Livetime = livetime;
            Iterations = iterations;            
            Detector = det;
            DetectorType = detType;
            LoadGEFactor();

            if (!Directory.Exists(sessionPath + Path.DirectorySeparatorChar + Name))
                Directory.CreateDirectory(sessionPath + Path.DirectorySeparatorChar + Name);
        }

        public void Add(Spectrum spec)
        {
            // Add a new spectrum to the list of spectrums

            Spectrums.Add(spec);
            NumChannels = spec.NumChannels;

            // Update state

            if (spec.MaxCount > MaxChannelCount)
                MaxChannelCount = spec.MaxCount;
            if (spec.MinCount < MinChannelCount)
                MinChannelCount = spec.MinCount;            
        }        

        public void Clear()
        {
            // Clear this session

            Name = String.Empty;
            Comment = String.Empty;
            Livetime = 0;
            Iterations = 0;
            Detector = null;
            DetectorType = null;            
            NumChannels = 0;
            MaxChannelCount = 0;
            MinChannelCount = 0;
            GEFactor = null;
            Spectrums.Clear();            
            Background = null;
        }

        public bool LoadGEFactor()
        {
            // Initialize GE factor function if GE script exists

            if (DetectorType == null)
                return false;

            if (!File.Exists(CrashEnvironment.GEScriptPath + Path.DirectorySeparatorChar + DetectorType.GEScript))
                return false;

            string script = File.ReadAllText(CrashEnvironment.GEScriptPath + Path.DirectorySeparatorChar + DetectorType.GEScript);
            dynamic scope = PyEngine.CreateScope();
            PyEngine.Execute(script, scope);
            GEFactor = scope.GetVariable<Func<double, double>>("GEFactor");
            return GEFactor != null;
        }

        public bool LoadSpectrums(string path)
        {
            // Load spectrums from a given path

            string jsonDir = path + Path.DirectorySeparatorChar + "json";
            if (!Directory.Exists(jsonDir))
                return false;

            string[] files = Directory.GetFiles(jsonDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (string filename in files)
            {
                string json = File.ReadAllText(filename);
                burn.Message msg = JsonConvert.DeserializeObject<burn.Message>(json);
                Spectrum spec = new Spectrum(msg);                                
                spec.CalculateDoserate(Detector, GEFactor);
                Add(spec);
            }

            Spectrums.Sort((a, b) => a.SessionIndex.CompareTo(b.SessionIndex));
            return true;
        }   
     
        public bool SetBackground(Session bkg)
        {
            // Set background counts for this session and adjust for livetime

            if (bkg == null)
            {
                Background = null;
                return true;
            }                

            if (IsEmpty || !IsLoaded)
                return false;
            
            Background = bkg.GetAdjustedCounts(Livetime);

            return true;
        }        

        private float[] GetAdjustedCounts(float livetime)
        {
            // Adjust counts for a given livetime

            if (Spectrums.Count < 1)
                return null;

            float[] spec = new float[(int)NumChannels];

            foreach(Spectrum s in Spectrums)            
                for (int i = 0; i < s.Channels.Count; i++)                
                    spec[i] += s.Channels[i];                                     
            
            float scale = livetime / Livetime;

            for (int i = 0; i < spec.Length; i++)
            {
                spec[i] /= (float)Spectrums.Count;
                spec[i] *= scale;                
            }

            return spec;
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

            float max = 0f;

            foreach (Spectrum s in Spectrums)
            {
                float curr = s.GetCountInROI(start, end);
                if (curr > max)
                    max = curr;
            }
            return max;
        }
    }    
}
