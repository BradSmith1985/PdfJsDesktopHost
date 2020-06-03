using System;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Caching;
using NHttp;
using System.IO;
using System.IO.Compression;
using System.Web;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;

namespace PdfJsDesktopHost {

    /// <summary>
    /// Provides a local HTTP server which can be used to preview documents using PDF.js
    /// </summary>
    public class Host : Component {

        /// <summary>
        /// How long (in minutes) to retain the association between a preview URL and the local filename.
        /// </summary>
		const int URL_LIFETIME_MINS = 30;
        /// <summary>
        /// The value used for the max-age directive in the Cache-Control response header.
        /// </summary>
        const int CACHE_MAX_AGE = 86400;
        /// <summary>
        /// The name of the manifest resource containing the PDF.js zip archive.
        /// </summary>
        const string RESOURCE_NAME = "PdfJsDesktopHost.pdfjs.zip";
        /// <summary>
        /// The name of the temp directory used by the server.
        /// </summary>
        const string TEMPDIR_NAME = "PdfJsDesktopHost";

        HttpServer _server;
        Task _extractTempFilesTask;

        /// <summary>
        /// Gets the full path to the temp directory used by the server.
        /// </summary>
        private string TempDirectory => Path.Combine(Path.GetTempPath(), TEMPDIR_NAME);

        /// <summary>
        /// Initialises a new instance of the <see cref="Host"/> class.
        /// </summary>
        public Host() {
            // set up server
            _server = new HttpServer();
			_server.RequestReceived += Server_RequestReceived;
			_server.UnhandledException += Server_UnhandledException;

            // extract temp files (async)
            _extractTempFilesTask = ExtractTempFilesAsync();
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="Host"/> class and adds it to the specified <see cref="IContainer"/>.
        /// </summary>
        /// <param name="container"></param>
        public Host(IContainer container) : this() {
            container.Add(this);
        }

        /// <summary>
        /// Starts the HTTP server if it is not currently running.
        /// </summary>
        private void EnsureStarted() {
            _extractTempFilesTask.Wait();

            switch (_server.State) {
                case HttpServerState.Stopped:
                case HttpServerState.Stopping:
                    _server.Start();
                    break;
            }
		}

        /// <summary>
        /// Returns a URL that can be used to preview the specified local file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
		public string GetUrlForDocument(string path) {
			// make sure server is running
			EnsureStarted();

            return GetUrlForObject(path);
		}

        /// <summary>
        /// Returns a URL that can be used to preview a document, using a delegate that returns a <see cref="Stream"/>.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        /// <remarks>
        /// The delegate is invoked each time the document is requested, and the stream is closed after each request.
        /// </remarks>
        public string GetUrlForDocument(Func<Stream> action) {
            // make sure server is running
            EnsureStarted();

            return GetUrlForObject(action);
        }

        private string GetUrlForObject(object value) {
            // assign unique ID
            Guid guid = Guid.NewGuid();
			string key = guid.ToString();

            // cache object
			MemoryCache.Default.Add(key, value, new CacheItemPolicy() { SlidingExpiration = TimeSpan.FromMinutes(URL_LIFETIME_MINS) });

            // construct viewer URL from unique ID
            return String.Format("http://localhost:{0}/web/viewer.html?file=%2Fdoc%2F{1}.pdf", _server.EndPoint.Port, key);
		}

		private async Task ExtractTempFilesAsync() {
            Directory.CreateDirectory(TempDirectory);

            using (Stream s = typeof(Host).Assembly.GetManifestResourceStream(RESOURCE_NAME)) {
                using (ZipArchive zip = new ZipArchive(s)) {
                    foreach (var entry in zip.Entries) {
                        string localDir = Path.Combine(TempDirectory, Path.GetDirectoryName(entry.FullName));
                        Directory.CreateDirectory(localDir);

                        if (String.IsNullOrWhiteSpace(entry.Name)) continue;

                        using (Stream src = entry.Open()) {
                            using (Stream dest = File.Open(Path.Combine(localDir, entry.Name), FileMode.Create, FileAccess.Write, FileShare.Read)) {
                                await src.CopyToAsync(dest).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }

        private void Server_UnhandledException(object sender, HttpExceptionEventArgs e) {
            if (Debugger.IsAttached) Debugger.Break();

            e.Response.StatusCode = 500;
            e.Response.StatusDescription = "Internal Server Error";
            e.Handled = true;
        }

        private void Server_RequestReceived(object sender, HttpRequestEventArgs e) {
            bool isHead = false;

            // check request method
            switch (e.Request.HttpMethod.ToUpperInvariant()) {
                case "GET":
                    break;
                case "HEAD":
                    isHead = true;
                    break;
                case "OPTIONS":
                    e.Response.StatusCode = 204;
                    e.Response.StatusDescription = "No Content";
                    e.Response.Headers["Allow"] = "OPTIONS, GET, HEAD";
                    return;
                default:
                    e.Response.StatusCode = 405;
                    e.Response.StatusDescription = "Method Not Allowed";
                    return;
            }

            bool success = false;
            string localPath = null;
            Func<Stream> action = null;

            if (e.Request.Path.StartsWith("/doc/", StringComparison.OrdinalIgnoreCase)) {
                // serve the PDF document with the specified ID
                string filename = e.Request.Path.Split('/').Last();

                if (filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) {
                    string key = Path.GetFileNameWithoutExtension(filename);
                    object cached = MemoryCache.Default.Get(key);
                    localPath = cached as string;
                    action = cached as Func<Stream>;
                }
            }
            else {
                // serve the requested static file from the temp directory
                localPath = Path.Combine(TempDirectory, e.Request.Path.TrimStart('/').Replace('/', '\\'));
            }

            if (!String.IsNullOrEmpty(localPath)) {
                // ensure file exists
                FileInfo info = new FileInfo(localPath);

                if (info.Exists) {
                    string modSince = e.Request.Headers["If-Modified-Since"];
                    DateTime lastModified;

                    if (!String.IsNullOrEmpty(modSince) && DateTime.TryParseExact(modSince, "R", Thread.CurrentThread.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out lastModified)) {
                        if (info.LastWriteTimeUtc <= lastModified) {
                            // allow client to use cached version
                            e.Response.StatusCode = 304;
                            e.Response.StatusDescription = "Not Modified";
                            success = true;
                        }
                    }

                    if (!success) {
                        // send normal response
                        e.Response.ContentType = MimeMapping.GetMimeMapping(Path.GetFileName(localPath));
                        e.Response.CacheControl = String.Format("public, max-age={0}", CACHE_MAX_AGE);
                        e.Response.Headers["Last-Modified"] = info.LastWriteTimeUtc.ToString("R");
                        e.Response.ExpiresAbsolute = info.LastWriteTimeUtc.AddSeconds(CACHE_MAX_AGE);

                        if (!isHead) {
                            using (Stream src = File.Open(localPath, FileMode.Open, FileAccess.Read)) {
                                src.CopyTo(e.Response.OutputStream);
                            }
                        }

                        success = true;
                    }
                }
            }
            else if (action != null) {
                // no last modified date if using stream
                e.Response.ContentType = "application/pdf";
                e.Response.CacheControl = String.Format("public, max-age={0}", CACHE_MAX_AGE);

                if (!isHead) {
                    using (Stream stream = action()) {
                        stream.CopyTo(e.Response.OutputStream);
                    }
                }

                success = true;
            }

            if (!success) {
                // return 404 for all other requests
                e.Response.StatusCode = 404;
                e.Response.StatusDescription = "Not Found";
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Host" />
		/// and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                // shut down server
                switch (_server.State) {
                    case HttpServerState.Started:
                        _server.Stop();                        
                        break;
                }

                _server.Dispose();

                // clean up temp files
                _extractTempFilesTask.Wait();
                _extractTempFilesTask.Dispose();

                if (Directory.Exists(TempDirectory)) {
                    Directory.Delete(TempDirectory, true);
                }                
            }

            base.Dispose(disposing);
        }
    }
}
