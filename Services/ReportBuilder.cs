using System.IO;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;

namespace HouseholdMS.Services
{
    public static class ReportBuilderPdf
    {
        public static void WriteSimplePdf(string path, string title, System.Tuple<string, string>[] kv)
        {
            using (var doc = new PdfDocument())
            {
                PdfPage page = doc.Pages.Add();
                PdfGraphics g = page.Graphics;

                PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
                PdfFont textFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11);

                g.DrawString(title, titleFont, PdfBrushes.Black, new PointF(0, 0));

                float y = 30f;
                for (int i = 0; i < kv.Length; i++)
                {
                    g.DrawString(kv[i].Item1 + ":", textFont, PdfBrushes.DarkBlue, new PointF(0, y));
                    g.DrawString(kv[i].Item2, textFont, PdfBrushes.Black, new PointF(150, y));
                    y += 18f;
                }

                // Save expects a Stream in this package version
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    doc.Save(fs);
                }
            }
        }
    }
}
