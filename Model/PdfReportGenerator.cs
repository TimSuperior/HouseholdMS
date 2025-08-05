using HouseholdMS.Model;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using System;
using System.IO;
using System.Linq;

public static class PdfReportGenerator
{
    public static void GenerateTestReportPDF(TestReport report, string filePath)
    {
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Mzk3NTExMEAzMzMwMmUzMDJlMzAzYjMzMzAzYlZIMmI2R1J4SGJTT0ExYWF0VTR2L3RMaDJEVUJyNkk2elh1YXpNSWFrSzA9;Mzk3NTExMUAzMzMwMmUzMDJlMzAzYjMzMzAzYmZIUkhmT1JKVzRZNDVKeUtra1BnanozdU5NTUtzeGM2MUNrY2Y0T3laN3c9;Mgo+DSMBPh8sVXN0S0d+X1ZPd11dXmJWd1p/THNYflR1fV9DaUwxOX1dQl9mSXlQd0djW31bdHVWQGRXUkQ=;NRAiBiAaIQQuGjN/VkZ+XU9HcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTcUZiW39ccnFRR2ZbV091Xw==;Mgo+DSMBMAY9C3t3VVhhQlJDfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5UdURhWX1cdXBUTmNfWkd2;Mzk3NTExNUAzMzMwMmUzMDJlMzAzYjMzMzAzYkhIbUxNNFR5alVJbys5YkVKdHJHVmYwL1p6ZnZrZ1hkaEQ1alZZQlVWVGs9;Mzk3NTExNkAzMzMwMmUzMDJlMzAzYjMzMzAzYmNwQ2s0ZWc5RzJab2l0ZFArM2R2VGIyWWorek1WenBOaHlPdjN2dnpmOGs9");

        var doc = new PdfDocument();
        var page = doc.Pages.Add();
        var g = page.Graphics;
        float y = 24, left = 40;
        float width = page.GetClientSize().Width - 2 * left;
        var fontHeader = new PdfStandardFont(PdfFontFamily.Helvetica, 24, PdfFontStyle.Bold);
        var fontSection = new PdfStandardFont(PdfFontFamily.Helvetica, 15, PdfFontStyle.Bold);
        var fontLabel = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        var fontNormal = new PdfStandardFont(PdfFontFamily.Helvetica, 12);
        var fontItalic = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Italic);
        var darkBlue = new PdfSolidBrush(Color.FromArgb(44, 62, 80));
        var sectionGreen = new PdfSolidBrush(Color.FromArgb(50, 150, 60));
        var sectionGray = new PdfSolidBrush(Color.FromArgb(230, 236, 240));
        var sectionRed = new PdfSolidBrush(Color.FromArgb(210, 30, 60));
        var lightYellow = new PdfSolidBrush(Color.FromArgb(255, 250, 205));
        var lightBlue = new PdfSolidBrush(Color.FromArgb(235, 245, 255));
        var labelGray = PdfBrushes.DimGray;

        // 1. Header Bar (full-width)
        g.DrawRectangle(darkBlue, new RectangleF(0, 0, page.GetClientSize().Width, 52));
        g.DrawString("☀ Solar Inspection Report", fontHeader, PdfBrushes.White, new PointF(left, 13));
        y = 58;

        // 2. Info Card
        g.DrawString("General Information", fontSection, sectionGreen, new PointF(left, y));
        y += 26;
        g.DrawString("Basic details about this inspection record:", fontNormal, labelGray, new PointF(left, y));
        y += 16;
        float infoHeight = 68;
        g.DrawRectangle(sectionGray, new RectangleF(left, y, width, infoHeight));
        y += 10;
        g.DrawString($"Report ID:", fontLabel, PdfBrushes.Black, new PointF(left + 12, y));
        g.DrawString(report?.ReportID.ToString() ?? "2", fontNormal, PdfBrushes.Black, new PointF(left + 90, y));
        g.DrawString("Household:", fontLabel, PdfBrushes.Black, new PointF(left + 200, y));
        g.DrawString(report?.HouseholdID.ToString() ?? "1001", fontNormal, PdfBrushes.Black, new PointF(left + 285, y));
        y += 20;
        g.DrawString("Technician:", fontLabel, PdfBrushes.Black, new PointF(left + 12, y));
        g.DrawString(report?.TechnicianID.ToString() ?? "7", fontNormal, PdfBrushes.Black, new PointF(left + 90, y));
        g.DrawString("Date:", fontLabel, PdfBrushes.Black, new PointF(left + 200, y));
        g.DrawString((report?.TestDate ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm"), fontNormal, PdfBrushes.Black, new PointF(left + 285, y));
        y += 28 + 10;

        // 3. Inspection Items
        g.DrawString("Inspection Items", fontSection, sectionGreen, new PointF(left, y));
        y += 26;
        g.DrawString("Devices or components checked during this inspection:", fontNormal, labelGray, new PointF(left, y));
        y += 16;

        float gridStartY = y;
        // Table header
        float col1 = left, col2 = left + 160, col3 = left + 320, rowHeight = 20;
        g.DrawRectangle(PdfBrushes.LightGray, new RectangleF(col1, y, width, rowHeight));
        g.DrawString("Item", fontLabel, PdfBrushes.Black, new PointF(col1 + 8, y + 3));
        g.DrawString("Result", fontLabel, PdfBrushes.Black, new PointF(col2 + 8, y + 3));
        g.DrawString("Annotation", fontLabel, PdfBrushes.Black, new PointF(col3 + 8, y + 3));
        y += rowHeight;

        var fakeItems = report?.InspectionItems ?? new System.Collections.Generic.List<HouseholdMS.Model.InspectionItem>
        {
            new HouseholdMS.Model.InspectionItem { Name = "Solar Panel", Result = "OK", Annotation = "Cleaned surface" },
            new HouseholdMS.Model.InspectionItem { Name = "Inverter", Result = "Warning", Annotation = "Minor noise detected" },
            new HouseholdMS.Model.InspectionItem { Name = "Battery", Result = "OK", Annotation = "" },
        };
        foreach (var item in fakeItems)
        {
            g.DrawRectangle(PdfBrushes.White, new RectangleF(col1, y, width, rowHeight));
            g.DrawString(item?.Name ?? "", fontNormal, PdfBrushes.Black, new PointF(col1 + 8, y + 3));
            g.DrawString(item?.Result ?? "", fontNormal, PdfBrushes.Black, new PointF(col2 + 8, y + 3));
            g.DrawString(item?.Annotation ?? "", fontNormal, PdfBrushes.Black, new PointF(col3 + 8, y + 3));
            y += rowHeight;
        }
        y += 10;

        // 4. Photos
        g.DrawString("Photos", fontSection, sectionRed, new PointF(left, y));
        y += 26;
        g.DrawString("Images captured at the site (click for full-size):", fontNormal, labelGray, new PointF(left, y));
        y += 16;

        var fakeImages = report?.ImagePaths ?? new System.Collections.Generic.List<string> { "sample_photo_path.jpg" };
        int imgSize = 100;
        int imgPad = 10;
        int imgPerRow = Math.Max(1, (int)(width / (imgSize + imgPad)));
        int count = 0;
        float imgY = y;
        foreach (var imgPath in fakeImages)
        {
            // For demo, use provided images if they exist, else skip (add your own path check or use a placeholder).
            string path = imgPath?.Trim();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    using (var imgStream = File.OpenRead(path))
                    {
                        var pdfImg = new PdfBitmap(imgStream);
                        float x = left + (count % imgPerRow) * (imgSize + imgPad);
                        g.DrawImage(pdfImg, new RectangleF(x, imgY, imgSize, imgSize));
                    }
                }
                catch { }
            }
            else
            {
                // Draw a placeholder if image missing
                float x = left + (count % imgPerRow) * (imgSize + imgPad);
                g.DrawRectangle(PdfPens.Gray, new RectangleF(x, imgY, imgSize, imgSize));
                g.DrawString("No Image", fontItalic, PdfBrushes.Gray, new PointF(x + 10, imgY + 40));
            }
            count++;
            if (count % imgPerRow == 0)
                imgY += imgSize + imgPad;
        }
        y = imgY + imgSize + 10;

        // 5. Settings Verification (dynamic, modern)
        g.DrawString("Settings Verification", fontSection, PdfBrushes.Navy, new PointF(left, y));
        y += 26;
        g.DrawString("Electrical or functional settings checked during inspection:", fontNormal, labelGray, new PointF(left, y));
        y += 16;

        string settingsText = (report?.SettingsVerification != null && report.SettingsVerification.Count > 0)
            ? string.Join(Environment.NewLine, report.SettingsVerification.Select(s => $"{s.Parameter}: {s.Value} [{s.Status}]"))
            : "Voltage=220V, Freq=60Hz, PowerLimit=5000W: OK";
        var settingsElem = new PdfTextElement(settingsText, fontNormal);
        var settingsLayout = settingsElem.Draw(page, new RectangleF(left + 8, y + 8, width - 16, 1000));
        float settingsHeight = settingsLayout.Bounds.Height + 16;
        g.DrawRectangle(lightBlue, new RectangleF(left, y, width, settingsHeight));
        settingsElem.Draw(page, new PointF(left + 8, y + 8));
        y += settingsHeight + 8;

        // 6. Notes (dynamic, modern)
        g.DrawString("Notes", fontSection, PdfBrushes.Brown, new PointF(left, y));
        y += 26;
        g.DrawString("Technician's additional notes, warnings, or important comments:", fontNormal, labelGray, new PointF(left, y));
        y += 16;

        string notesText = (report?.Annotations != null && report.Annotations.Count > 0)
            ? string.Join(Environment.NewLine, report.Annotations)
            : "• Inspection performed successfully\n• No major issues found\n• Next maintenance recommended after 6 months";
        var notesElem = new PdfTextElement(notesText, fontNormal);
        var notesLayout = notesElem.Draw(page, new RectangleF(left + 8, y + 8, width - 16, 1000));
        float notesHeight = notesLayout.Bounds.Height + 16;
        g.DrawRectangle(lightYellow, new RectangleF(left, y, width, notesHeight));
        notesElem.Draw(page, new PointF(left + 8, y + 8));
        y += notesHeight + 12;

        // 7. Device Status (with annotation)
        g.DrawString("Device Status:", fontLabel, sectionGreen, new PointF(left, y));
        g.DrawString((report?.DeviceStatus ?? "MPPT Controller Connected"), fontLabel, PdfBrushes.Black, new PointF(left + 120, y));
        y += 26;

        g.DrawString("Current operational status of main controller or solar system.", fontNormal, labelGray, new PointF(left, y));
        y += 22;

        // 8. Technician Signature (underline only, not box!)
        float sigLeft = left + 140;
        g.DrawString("Technician Signature:", fontNormal, PdfBrushes.Black, new PointF(left, y + 6));
        g.DrawLine(PdfPens.Black, new PointF(sigLeft, y + 22), new PointF(sigLeft + 200, y + 22));
        g.DrawString("Date:", fontNormal, PdfBrushes.Black, new PointF(sigLeft + 220, y + 6));
        g.DrawLine(PdfPens.Black, new PointF(sigLeft + 260, y + 22), new PointF(sigLeft + 340, y + 22));
        y += 40;

        // Optionally, add a footer, logo, or more styling here

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            doc.Save(fs);
        doc.Close(true);
    }
}
