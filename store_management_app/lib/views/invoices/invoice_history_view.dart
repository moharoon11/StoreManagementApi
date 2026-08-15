import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class InvoiceHistoryView extends StatefulWidget {
  const InvoiceHistoryView({Key? key}) : super(key: key);

  @override
  State<InvoiceHistoryView> createState() => _InvoiceHistoryViewState();
}

class _InvoiceHistoryViewState extends State<InvoiceHistoryView> {
  bool _isLoading = true;
  List<dynamic> _invoices = [];

  @override
  void initState() {
    super.initState();
    _fetchInvoices();
  }

  Future<void> _fetchInvoices() async {
    setState(() => _isLoading = true);
    try {
      final res = await ApiService.get(ApiConfig.invoices, queryParameters: {'pageSize': '50'});
      if (res['success'] == true) {
        setState(() {
          _invoices = res['data']['items'] ?? [];
          _isLoading = false;
        });
      }
    } catch (_) {
      setState(() => _isLoading = false);
    }
  }

  Future<void> _openPdf(int invoiceId) async {
    final pdfUrl = '${ApiConfig.baseUrl}${ApiConfig.invoices}/$invoiceId/pdf';
    final uri = Uri.parse(pdfUrl);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    } else {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('PDF URL: $pdfUrl')),
        );
      }
    }
  }

  void _showInvoiceDetail(Map<String, dynamic> invoice) {
    final items = (invoice['items'] as List?) ?? [];

    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: const Color(0xFF1E293B),
        title: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text('Invoice ${invoice['invoiceNumber']}', style: const TextStyle(color: Colors.white, fontSize: 18)),
            IconButton(
              icon: const Icon(Icons.picture_as_pdf, color: Colors.redAccent),
              onPressed: () => _openPdf(invoice['id']),
              tooltip: 'Download PDF Invoice',
            ),
          ],
        ),
        content: SizedBox(
          width: 500,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text('Grand Total: ₹${invoice['grandTotal']}', style: const TextStyle(color: Colors.greenAccent, fontSize: 18, fontWeight: FontWeight.bold)),
                  Text('${invoice['createdAt']?.toString().split('T').first}', style: const TextStyle(color: Colors.grey)),
                ],
              ),
              const Divider(color: Color(0xFF334155)),
              const SizedBox(height: 8),
              ...items.map((item) {
                return ListTile(
                  dense: true,
                  title: Text(item['productName'] ?? '', style: const TextStyle(color: Colors.white)),
                  subtitle: Text('Qty: ${item['quantity']} × ₹${item['sellingPrice']}', style: const TextStyle(color: Colors.grey)),
                  trailing: Text('₹${item['total']}', style: const TextStyle(color: Colors.greenAccent, fontWeight: FontWeight.bold)),
                );
              }).toList(),
            ],
          ),
        ),
        actions: [
          ElevatedButton.icon(
            onPressed: () => _openPdf(invoice['id']),
            icon: const Icon(Icons.picture_as_pdf),
            label: const Text('Download / Share PDF'),
            style: ElevatedButton.styleFrom(backgroundColor: Colors.redAccent),
          ),
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Close'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator(color: Colors.blueAccent));
    }

    return Padding(
      padding: const EdgeInsets.all(24.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Invoice History', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white)),
          const SizedBox(height: 24),
          Expanded(
            child: _invoices.isEmpty
                ? const Center(child: Text('No invoices generated yet.', style: TextStyle(color: Colors.grey)))
                : ListView.builder(
                    itemCount: _invoices.length,
                    itemBuilder: (context, index) {
                      final inv = _invoices[index];
                      return Card(
                        color: const Color(0xFF1E293B),
                        margin: const EdgeInsets.only(bottom: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        child: ListTile(
                          onTap: () => _showInvoiceDetail(inv),
                          leading: CircleAvatar(
                            backgroundColor: Colors.blue.withOpacity(0.1),
                            child: const Icon(Icons.receipt, color: Colors.blueAccent),
                          ),
                          title: Text(inv['invoiceNumber'] ?? '', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                          subtitle: Text('Date: ${inv['createdAt']?.toString().replaceAll('T', ' ').substring(0, 16)}', style: const TextStyle(color: Colors.grey)),
                          trailing: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text('₹${inv['grandTotal']}', style: const TextStyle(color: Colors.greenAccent, fontSize: 16, fontWeight: FontWeight.bold)),
                              const SizedBox(width: 12),
                              IconButton(
                                icon: const Icon(Icons.picture_as_pdf, color: Colors.redAccent),
                                onPressed: () => _openPdf(inv['id']),
                              ),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
