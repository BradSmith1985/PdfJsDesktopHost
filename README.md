# PdfJsDesktopHost
A self-contained, lightweight, local HTTP server which can be used by desktop applications to preview documents using [PDF.js](https://mozilla.github.io/pdf.js/).

The most common use case for this project is for previewing PDFs inside a Windows Forms application, using the `WebBrowser` control. You simply add the `Host` component to the form, call the `GetUrlForDocument()` method and then navigate the browser to the resulting address. This approach has some significant benefits over alternatives such as Windows Preview Handlers and ActiveX controls, in that it requires no special software to be installed on the client machine and always gives a consistent user experience.

## Dependencies
 - .NET Framework 4.5+
 - [NHttp](https://github.com/pvginkel/NHttp) (more lightweight HTTP server, does not require URL reservations unlike `http.sys`)

## Updating PDF.js
This repository includes a stable release of PDF.js built for ES5 browsers (including IE11, the main target for this project). If you wish to take advantage of the latest version of PDF.js, simply overwrite the `pdfjs.zip` file in the source directory with a newer version obtained from the Mozilla Labs website.
