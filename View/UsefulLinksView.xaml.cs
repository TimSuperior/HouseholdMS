using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class UsefulLinksView : UserControl, INotifyPropertyChanged
    {
        public ObservableCollection<LinkGroup> Groups { get; } = new ObservableCollection<LinkGroup>();
        public int TotalResourceCount => Groups.Sum(g => g.Items.Sum(i => i.Resources.Count));

        public UsefulLinksView()
        {
            InitializeComponent();
            BuildModel();      // crea el modelo y detecta archivos locales
            DataContext = this;
        }

        /* =======================  EDITAR AQUÍ (modelo)  =======================
         * Archivos en: Assets/Links  (raíz, sin subcarpetas; según tu captura)
         * Se agregan automáticamente por prefijo. Los títulos legibles en español
         * para cada PDF se asignan en FileTitleMap (abajo).
         */

        private void BuildModel()
        {
            var items = new List<LinkItem>
            {
                // 👉 NUEVO: Excel con listado de todos los hogares — detecta Households*.xlsx/.xls
                new LinkItem
                {
                    Group = "Datos",
                    Title = "Listado de hogares (Excel)",
                    Description = "Archivo Excel con la información de todos los hogares.",
                    Icon = "📊",
                    FilePrefix = "1_400" // coloca Households.xlsx en Assets/Links
                },

                // 1) Blue Smart IP22 Charger — web + PDFs con prefijo "Victron"
                new LinkItem
                {
                    Group = "Cargadores",
                    Title = "Blue Smart IP22 Charger",
                    Description = "Cargador Victron Energy con Bluetooth.",
                    Icon = "🔌",
                    FilePrefix = "Victron",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.victronenergy.com.es/chargers/blue-smart-ip22-charger") }
                },

                // 2) ISDT K4 Smart Charger — PDF "K4_*.pdf" + 2 enlaces
                new LinkItem
                {
                    Group = "Cargadores",
                    Title = "ISDT K4 Smart Charger",
                    Description = "Cargador inteligente ISDT K4.",
                    Icon = "🔌",
                    FilePrefix = "K4",
                    Resources =
                    {
                        LinkResource.Web("Manual en línea", "https://manuals.plus/isdt/k4-smart-charger-manual#MTQ4LjExMy4yMTAuMjUwOzJhMDY6OThjMDozNjAwOjoxMDMsIDE3Mi43MS4xNDcuMTQ1LCAxMDQuMTMxLjE2OS4xMTU7MTA0LjEzMS4xNjkuMTE1OzJhMDY6OThjMDozNjAwOjoxMDM7"),
                        LinkResource.Web("App de ISDT", "https://www.isdt.co/%E8%BD%AF%E4%BB%B6%E5%BA%94%E7%94%A8?lang=en")
                    }
                },

                // 3) Fuente DC — PDF "DCPowerSup*.pdf" + enlace
                new LinkItem
                {
                    Group = "Fuentes de alimentación DC",
                    Title = "Fuente de alimentación DC",
                    Description = "Modelo TS1001 (ver manual y ficha del producto).",
                    Icon = "🔋",
                    FilePrefix = "DCPowerSup",
                    Resources = { LinkResource.Web("Ficha del producto", "https://www.testermart.com/goods/goods_view.php?goodsNo=1000002890&srsltid=AfmBOoqMKpPZxP5p-aPz0pnmJN7ndIqLvvU3FtutDwUv0lc5mQhQcCLq") }
                },

                // 4) Multímetro MP730889 — PDF "Multimeter_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Instrumentación",
                    Title = "Multímetro digital MP730889",
                    Description = "Multicomp Pro MP730889 (de banco).",
                    Icon = "🧪",
                    FilePrefix = "Multimeter",
                    Resources = { LinkResource.Web("Ficha (Farnell)", "https://es.farnell.com/multicomp-pro/mp730889/dmm-bench-10a-1kv-50mohm/dp/3972198?srsltid=AfmBOopZJ8sfRJs0ezFoHAXoT-rZ2huFf-OwsHhJla0aMSibkGnF5QNm") }
                },

                // 5) Tektronix TBS1000C — PDF "TBS1000C*.pdf" + enlace
                new LinkItem
                {
                    Group = "Osciloscopios",
                    Title = "Tektronix TBS1000C",
                    Description = "Serie TBS1000C de 2 canales.",
                    Icon = "📈",
                    FilePrefix = "TBS1000C",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.tek.com/en/products/oscilloscopes/tbs1000-2-channel-digital-storage-oscilloscope") }
                },

                // 6) Inversor DK2410A — solo enlace
                new LinkItem
                {
                    Group = "Inversores",
                    Title = "Inversor DK2410A (onda senoidal pura)",
                    Description = "PNK HiTech DK2410A 24V.",
                    Icon = "⚡",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.pnkhitech.co.kr/02_product_eng/product01_24v_2410.php") }
                },

                // 7) IT8600 — PDF "IT8600*.pdf" + enlace
                new LinkItem
                {
                    Group = "Cargas electrónicas",
                    Title = "IT8600 Carga electrónica AC",
                    Description = "Serie IT8600 de ITECH.",
                    Icon = "🧰",
                    FilePrefix = "IT8600",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.itechate.com/en/product/ac-electronic-load/IT8600.html") }
                },

                // 8) EPEVER XTRA-N G3 — PDF "MPPT_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Controladores solares",
                    Title = "EPEVER XTRA-N G3 (MPPT)",
                    Description = "Controlador de carga MPPT XTRA-N G3.",
                    Icon = "☀️",
                    FilePrefix = "MPPT",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.epever.com/product/xtra-n-g3-mppt-charge-controller/") }
                },

                // 9) HP Smart Tank 720 — PDFs "HPSmartTankseries_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Impresoras",
                    Title = "HP Smart Tank serie 720",
                    Description = "Soporte y guías del modelo.",
                    Icon = "🖨️",
                    FilePrefix = "HPSmartTankseries",
                    Resources = { LinkResource.Web("Soporte oficial", "https://support.hp.com/cl-es/product/setup-user-guides/hp-smart-tank-720-series/2100043634") }
                },

                // 10) Fluke 376 FC — PDF "376FC_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Instrumentación",
                    Title = "Fluke 376 FC (pinza amperimétrica)",
                    Description = "Pinza amperimétrica con conectividad.",
                    Icon = "🧲",
                    FilePrefix = "376FC",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.fluke.com/es-cr/producto/comprobacion-electrica/pinzas-amperimetricas/fluke-376-fc") }
                },

                // 11) Fluke 87V Max — PDF "MaxMultimetroDigital_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Instrumentación",
                    Title = "Fluke 87V Max (multímetro digital)",
                    Description = "Multímetro digital industrial.",
                    Icon = "🧪",
                    FilePrefix = "MaxMultimetroDigital",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.fluke.com/es-es/producto/comprobacion-electrica/multimetros-digitales/87v-max#") }
                },

                // 12) Bosch GTC 600 C — PDFs "GTC600_*.pdf" + enlace
                new LinkItem
                {
                    Group = "Instrumentación",
                    Title = "Bosch GTC 600 C (termocámara)",
                    Description = "Cámara termográfica profesional.",
                    Icon = "🔥",
                    FilePrefix = "GTC600",
                    Resources = { LinkResource.Web("Sitio oficial", "https://www.bosch-professional.com/mx/es/products/gtc-600-c-06010835K1") }
                },
            };

            // Agrupar y adjuntar archivos locales por prefijo
            var grouped = items.GroupBy(i => i.Group ?? "Otros").OrderBy(g => g.Key);
            Groups.Clear();
            foreach (var g in grouped)
            {
                var grp = new LinkGroup { Header = g.Key };
                foreach (var item in g)
                {
                    AttachLocalFiles(item);
                    grp.Items.Add(item);
                }
                Groups.Add(grp);
            }

            OnPropertyChanged(nameof(Groups));
            OnPropertyChanged(nameof(TotalResourceCount));
        }

        // ✅ C# 7.3 compatible: constructor explícito (sin target-typed new)
        private static readonly Dictionary<string, string> FileTitleMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Etiquetas legibles existentes
                {"VictronUserMan_es.pdf",              "Manual de usuario (ES)"},
                {"VictronConnect.pdf",                 "Victron Connect (aplicación)"},
                {"K4_userman_ch.pdf",                  "Manual ISDT K4"},
                {"DCPowerSupTS1001_userman_kr.pdf",    "Manual fuente DC TS1001"},
                {"MPPT_UserMan_En.pdf",                "Manual EPEVER XTRA-N G3 (EN)"},
                {"Multimeter_UserMan_en.pdf",          "Manual multímetro MP730889 (EN)"},
                {"TBS1000COscilloscopeUserMan_en.pdf", "Manual Tektronix TBS1000C (EN)"},
                {"376FC_ClampMeter_UserMan.pdf",       "Manual Fluke 376 FC"},
                {"MaxMultimetroDigital_UserManEs.pdf", "Manual Fluke 87V Max (ES)"},
                {"GTC600_QuickST.pdf",                 "Bosch GTC 600 C — Guía rápida"},
                {"GTC600_UserMan.pdf",                 "Manual Bosch GTC 600 C"},
                {"HPSmartTankseries_Setup Guide.pdf",  "HP Smart Tank 720 — Guía de instalación"},
                {"HPSmartTankseries_UserMan_en.pdf",   "HP Smart Tank 720 — Manual (EN)"},
                {"IT8600UserManual_en.pdf",            "Manual IT8600 (EN)"},

                // 👉 NUEVO: etiquetas para el Excel de hogares (por si quieres nombre exacto)
                {"Households.xlsx",                    "Listado de hogares (Excel)"},
                {"Households.xls",                     "Listado de hogares (Excel)"}
            };

        private static readonly string[] AllowedExtensions = new[] { ".pdf", ".xls", ".xlsx", ".doc", ".docx" };

        private void AttachLocalFiles(LinkItem item)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var linksRoot = Path.Combine(baseDir, "Assets", "Links");
                if (!Directory.Exists(linksRoot)) return;

                var files = new List<string>();

                if (!string.IsNullOrWhiteSpace(item.FilePrefix))
                {
                    var patternPrefix = Regex.Escape(item.FilePrefix);
                    foreach (var f in Directory.EnumerateFiles(linksRoot))
                    {
                        var ext = Path.GetExtension(f);
                        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;

                        var name = Path.GetFileName(f);
                        if (Regex.IsMatch(name, "^" + patternPrefix + ".*", RegexOptions.IgnoreCase))
                            files.Add(f);
                    }
                }

                foreach (var fullPath in files.OrderBy(p => p))
                {
                    var fileName = Path.GetFileName(fullPath);

                    var label = FileTitleMap.ContainsKey(fileName)
                        ? FileTitleMap[fileName]
                        : ToPrettyTitle(fileName);

                    item.Resources.Add(LinkResource.File(label, fullPath));
                }
            }
            catch
            {
                // Ignorar errores de descubrimiento
            }
        }

        private static string ToPrettyTitle(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            name = name.Replace('_', ' ');
            name = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return name;
        }

        private void ResourceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LinkResource res)
            {
                try
                {
                    if (res.IsWeb)
                    {
                        OpenWithShell(res.Target);   // navegador predeterminado
                    }
                    else
                    {
                        if (File.Exists(res.Target))
                            OpenWithShell(res.Target); // app predeterminada (Excel para .xlsx/.xls)
                        else
                            MessageBox.Show("No se encontró el archivo:\n" + res.Target,
                                "Archivo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo abrir el recurso.\n\n" + ex.Message,
                        "Error al abrir", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void OpenWithShell(string target)
        {
            var psi = new ProcessStartInfo { FileName = target, UseShellExecute = true };
            Process.Start(psi);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class LinkGroup
    {
        public string Header { get; set; }
        public ObservableCollection<LinkItem> Items { get; } = new ObservableCollection<LinkItem>();
    }

    public class LinkItem
    {
        public string Group { get; set; } = "Otros";
        public string Title { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; } = "🔗";
        public string FilePrefix { get; set; }
        public ObservableCollection<LinkResource> Resources { get; } = new ObservableCollection<LinkResource>();
    }

    public class LinkResource
    {
        public bool IsWeb { get; private set; }
        public string Label { get; private set; }
        public string Target { get; private set; }
        public string Icon { get { return IsWeb ? "🌐" : "📄"; } }

        public string ShortTarget
        {
            get
            {
                if (IsWeb)
                {
                    try { return new Uri(Target).Host.Replace("www.", ""); }
                    catch { return Target; }
                }
                return Path.GetFileName(Target);
            }
        }

        public static LinkResource Web(string label, string url)
        {
            return new LinkResource { IsWeb = true, Label = label, Target = url };
        }

        public static LinkResource File(string label, string absolutePath)
        {
            return new LinkResource { IsWeb = false, Label = label, Target = absolutePath };
        }
    }
}
