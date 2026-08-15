import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class SalesReportsView extends StatefulWidget {
  const SalesReportsView({Key? key}) : super(key: key);

  @override
  State<SalesReportsView> createState() => _SalesReportsViewState();
}

class _SalesReportsViewState extends State<SalesReportsView> {
  bool _isLoading = true;
  String _selectedPeriod = 'today';
  Map<String, dynamic>? _reportData;

  @override
  void initState() {
    super.initState();
    _fetchReport();
  }

  Future<void> _fetchReport() async {
    setState(() => _isLoading = true);
    try {
      final res = await ApiService.get(
        ApiConfig.salesReports,
        queryParameters: {'period': _selectedPeriod},
      );
      if (res['success'] == true) {
        setState(() {
          _reportData = res['data'];
          _isLoading = false;
        });
      }
    } catch (_) {
      setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final totalSales = _reportData?['totalSales'] ?? 0;
    final totalInvoices = _reportData?['totalInvoices'] ?? 0;
    final totalProductsSold = _reportData?['totalProductsSold'] ?? 0;
    final topSoldProducts = (_reportData?['topSoldProducts'] as List?) ?? [];
    final salesByCategory = (_reportData?['salesByCategory'] as List?) ?? [];

    return Padding(
      padding: const EdgeInsets.all(24.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text('Sales Analytics & Reports', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white)),
              Row(
                children: ['today', 'week', 'month'].map((period) {
                  final isSelected = _selectedPeriod == period;
                  return Padding(
                    padding: const EdgeInsets.only(left: 8.0),
                    child: ChoiceChip(
                      label: Text(period.toUpperCase()),
                      selected: isSelected,
                      selectedColor: Colors.blueAccent,
                      labelStyle: TextStyle(color: isSelected ? Colors.white : Colors.grey[400]),
                      onSelected: (selected) {
                        if (selected) {
                          setState(() => _selectedPeriod = period);
                          _fetchReport();
                        }
                      },
                    ),
                  );
                }).toList(),
              ),
            ],
          ),
          const SizedBox(height: 24),
          if (_isLoading)
            const Expanded(child: Center(child: CircularProgressIndicator(color: Colors.blueAccent)))
          else
            Expanded(
              child: SingleChildScrollView(
                child: Column(
                  children: [
                    Row(
                      children: [
                        Expanded(child: _buildReportMetric('Total Revenue', '₹$totalSales', Icons.payments, Colors.greenAccent)),
                        const SizedBox(width: 16),
                        Expanded(child: _buildReportMetric('Total Invoices', '$totalInvoices', Icons.receipt, Colors.blueAccent)),
                        const SizedBox(width: 16),
                        Expanded(child: _buildReportMetric('Products Sold', '$totalProductsSold', Icons.shopping_bag, Colors.purpleAccent)),
                      ],
                    ),
                    const SizedBox(height: 32),
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: _buildTableCard('Top Sold Products', topSoldProducts, (p) {
                            return ListTile(
                              title: Text(p['productName'] ?? '', style: const TextStyle(color: Colors.white)),
                              subtitle: Text('${p['totalQuantitySold']} units sold', style: const TextStyle(color: Colors.grey)),
                              trailing: Text('₹${p['totalRevenue']}', style: const TextStyle(color: Colors.greenAccent, fontWeight: FontWeight.bold)),
                            );
                          }),
                        ),
                        const SizedBox(width: 16),
                        Expanded(
                          child: _buildTableCard('Sales by Category', salesByCategory, (c) {
                            return ListTile(
                              title: Text(c['categoryName'] ?? '', style: const TextStyle(color: Colors.white)),
                              subtitle: Text('${c['totalQuantitySold']} units sold', style: const TextStyle(color: Colors.grey)),
                              trailing: Text('₹${c['totalRevenue']}', style: const TextStyle(color: Colors.blueAccent, fontWeight: FontWeight.bold)),
                            );
                          }),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildReportMetric(String title, String value, IconData icon, Color color) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: Colors.white.withOpacity(0.05)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color, size: 30),
          const SizedBox(height: 12),
          Text(title, style: const TextStyle(color: Colors.grey, fontSize: 13)),
          const SizedBox(height: 4),
          Text(value, style: const TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.bold)),
        ],
      ),
    );
  }

  Widget _buildTableCard(String title, List<dynamic> items, Widget Function(dynamic) itemBuilder) {
    return Container(
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: Colors.white.withOpacity(0.05)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.all(16.0),
            child: Text(title, style: const TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold)),
          ),
          const Divider(height: 1, color: Color(0xFF334155)),
          items.isEmpty
              ? const Padding(padding: EdgeInsets.all(16), child: Text('No data recorded for this period.', style: TextStyle(color: Colors.grey)))
              : Column(children: items.map((item) => itemBuilder(item)).toList()),
        ],
      ),
    );
  }
}
