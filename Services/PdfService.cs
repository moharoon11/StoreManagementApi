using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StoreManagement.Api.Models;
using NLog;

namespace StoreManagement.Api.Services
{
    public class PdfService : IPdfService
    {
        private static readonly Logger Logger = LogManager.GetLogger("PdfService");
        public PdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerateInvoicePdf(Invoice invoice)
        {
            Logger.Debug("GenerateInvoicePdf started. InvoiceId: {0}, InvoiceNumber: {1}", invoice.Id, invoice.InvoiceNumber);
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                    page.Header().Element(headerContainer => ComposeHeader(headerContainer, invoice));
                    page.Content().Element(contentContainer => ComposeContent(contentContainer, invoice));
                    page.Footer().Element(footerContainer => ComposeFooter(footerContainer, invoice));
                });
            });

            var pdf = document.GeneratePdf();
            Logger.Info("GenerateInvoicePdf succeeded. InvoiceId: {0}, Bytes: {1}", invoice.Id, pdf.Length);
            return pdf;
        }

        private static void ComposeHeader(IContainer container, Invoice invoice)
        {
            var store = invoice.Store;

            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(store?.StoreName ?? "STORE MANAGEMENT").FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                    if (!string.IsNullOrEmpty(store?.OwnerName))
                        column.Item().Text($"Owner: {store.OwnerName}").FontSize(10);
                    if (!string.IsNullOrEmpty(store?.Address))
                        column.Item().Text(store.Address).FontSize(9);
                    if (!string.IsNullOrEmpty(store?.City) || !string.IsNullOrEmpty(store?.Pincode))
                        column.Item().Text($"{store?.City} - {store?.Pincode}").FontSize(9);
                    if (!string.IsNullOrEmpty(store?.Phone))
                        column.Item().Text($"Phone: {store.Phone}").FontSize(9);
                    if (!string.IsNullOrEmpty(store?.GstNumber))
                        column.Item().Text($"GSTIN: {store.GstNumber}").FontSize(9).Bold();
                });

                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().Text("TAX INVOICE").FontSize(22).Bold().FontColor(Colors.Grey.Darken2);
                    column.Item().Text($"Invoice No: {invoice.InvoiceNumber}").FontSize(11).Bold();
                    column.Item().Text($"Date: {invoice.CreatedAt:yyyy-MM-dd HH:mm}").FontSize(10);
                    if (!string.IsNullOrWhiteSpace(invoice.CustomerName))
                    {
                        column.Item().PaddingTop(6).Text("Bill To").FontSize(10).Bold();
                        column.Item().Text(invoice.CustomerName).FontSize(10);
                    }
                    if (!string.IsNullOrWhiteSpace(invoice.CustomerMobileNumber))
                        column.Item().Text($"Mobile: {invoice.CustomerMobileNumber}").FontSize(9);
                });
            });
        }

        private static void ComposeContent(IContainer container, Invoice invoice)
        {
            container.PaddingVertical(15).Column(column =>
            {
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                column.Item().PaddingVertical(10);

                // Invoice Table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(35);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    // Table Header
                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("#");
                        header.Cell().Element(HeaderCellStyle).Text("Product");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Price");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Qty");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Total");
                    });

                    // Table Rows
                    for (int i = 0; i < invoice.Items.Count; i++)
                    {
                        var item = invoice.Items[i];
                        var backgroundColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => CellStyle(c, backgroundColor)).Text((i + 1).ToString());
                        table.Cell().Element(c => CellStyle(c, backgroundColor)).Text(item.ProductName).Bold();
                        table.Cell().Element(c => CellStyle(c, backgroundColor)).AlignRight().Text($"₹{item.SellingPrice:N2}");
                        table.Cell().Element(c => CellStyle(c, backgroundColor)).AlignRight().Text(item.Quantity.ToString());
                        table.Cell().Element(c => CellStyle(c, backgroundColor)).AlignRight().Text($"₹{item.Total:N2}");
                    }
                });

                column.Item().PaddingVertical(10);

                // Total Summary Section
                column.Item().AlignRight().Column(summary =>
                {
                    summary.Item().Row(r =>
                    {
                        r.ConstantItem(150).Text("Subtotal:").FontSize(11);
                        r.ConstantItem(100).AlignRight().Text($"₹{invoice.Subtotal:N2}").FontSize(11);
                    });

                    summary.Item().PaddingTop(4).Row(r =>
                    {
                        r.ConstantItem(150).Text("Grand Total:").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                        r.ConstantItem(100).AlignRight().Text($"₹{invoice.GrandTotal:N2}").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                    });
                });
            });
        }

        private static void ComposeFooter(IContainer container, Invoice invoice)
        {
            container.Column(col =>
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                var storeName = string.IsNullOrWhiteSpace(invoice.Store?.StoreName)
                    ? "our store"
                    : invoice.Store.StoreName;
                col.Item().PaddingTop(8).AlignCenter().Text($"Thank you for shopping with {storeName}!").FontSize(11).Italic().FontColor(Colors.Grey.Darken1);
            });
        }

        private static IContainer HeaderCellStyle(IContainer container)
        {
            return container.Background(Colors.Grey.Lighten2).Padding(6).DefaultTextStyle(x => x.Bold());
        }

        private static IContainer CellStyle(IContainer container, string backgroundColor)
        {
            return container.Background(backgroundColor).Padding(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
        }
    }
}
