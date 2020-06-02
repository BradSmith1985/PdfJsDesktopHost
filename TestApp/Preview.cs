using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace TestApp {

	public partial class Preview : Form {		

		public Preview() {
			InitializeComponent();
		}

		private void exitToolStripMenuItem_Click(object sender, EventArgs e) {
			Close();
		}

		private void openToolStripMenuItem_Click(object sender, EventArgs e) {
			if (openFileDialog.ShowDialog() == DialogResult.OK) {
				// register the local file with the HTTP server
				string url = host.GetUrlForDocument(openFileDialog.FileName);
				//string url = host.GetUrlForDocument(() => File.OpenRead(openFileDialog.FileName));

				// open in embedded web browser
				webBrowser.Navigate(url);
			}
		}
	}
}
