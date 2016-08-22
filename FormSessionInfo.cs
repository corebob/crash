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
    public partial class FormSessionInfo : Form
    {
        private Session session = null;

        public FormSessionInfo(Session sess, string title)
        {
            InitializeComponent();
            Text = "Crash - " + title;
            session = sess;
        }

        private void FormSessionInfo_Load(object sender, EventArgs e)
        {
            lblName.Text = session.Info.Name;
            lblDetector.Text = session.Info.Detector.Serialnumber;
            lblLivetime.Text = session.Info.Livetime.ToString();
            tbComment.Text = session.Info.Comment;
            tbGEScript.Text = session.Info.GEScript;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            session.Info.Comment = tbComment.Text;
            Close();
        }        
    }
}