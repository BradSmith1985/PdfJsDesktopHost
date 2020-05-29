# PdfJsDesktopHost
A self-contained, lightweight, local HTTP server which can be used by desktop applications to preview documents using [PDF.js](https://mozilla.github.io/pdf.js/).

The most common use case for this project is for previewing PDFs inside a Windows Forms application, using the `WebBrowser` control. This approach has some significant benefits over alternatives such as Windows Preview Handlers and ActiveX controls, in that it requires no special software to be installed on the client machine and always gives a consistent user experience.
