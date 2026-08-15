import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class DashboardView extends StatefulWidget {
  const DashboardView({Key? key}) : super(key: key);

  @override
  State<DashboardView> createState() => _DashboardViewState();
}

class _DashboardViewState extends State<DashboardView> {
  bool _isLoading = true;
  Map<String, dynamic>? _dashboardData;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadDashboard();
  }

  Future<void> _loadDashboard() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final res = await ApiService.get(ApiConfig.dashboard);
      if (res['success'] == true) {
        setState(() {
          _dashboardData = res['data'];
          _isLoading = false;
        });
      } else {
        setState(() {
          _error = res['message'];
          _isLoading = false;
        });
      }
    } catch (e) {
      setState(() {
        _error = e.toString().replaceAll('Exception: ', '');
        _isLoading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator(color: Colors.blueAccent));
    }

    if (_error != null) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text('Error: $_error', style: const TextStyle(color: Colors.redAccent)),
            const SizedBox(height: 12),
            ElevatedButton(onPressed: _loadDashboard, child: const Text('Retry')),
          ],
        ),
      );
    }

    final todaySales = _dashboardData?['todaySales'] ?? 0;
    final todayInvoices = _dashboardData?['todayInvoiceCount'] ?? 0;
    final totalProducts = _dashboardData?['totalProducts'] ?? 0;
    final totalCategories = _dashboardData?['totalCategories'] ?? 0;
    final lowStockProducts = (_dashboardData?['lowStockProducts'] as List?) ?? [];
    final mostSoldProducts = (_dashboardData?['mostSoldProducts'] as List?) ?? [];
    final recentInvoices = (_dashboardData?['recentInvoices'] as List?) ?? [];

    return RefreshIndicator(
      onRefresh: _loadDashboard,
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                const Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Store Dashboard',
                      style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white),
                    ),
                    SizedBox(height: 4),
                    Text(
                      'Real-time overview of sales, stock & activity',
                      style: TextStyle(color: Colors.grey),
                    ),
                  ],
                ),
                IconButton(
                  icon: const Icon(Icons.refresh, color: Colors.blueAccent),
                  onPressed: _loadDashboard,
                ),
              ],
            ),
            const SizedBox(height: 24),
            // Metric Cards Grid
            LayoutBuilder(
              builder: (context, constraints) {
                int crossAxisCount = constraints.maxWidth > 900 ? 4 : (constraints.maxWidth > 600 ? 2 : 1);
                return GridView.count(
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  crossAxisCount: crossAxisCount,
                  crossAxisSpacing: 16,
                  mainAxisSpacing: 16,
                  childAspectRatio: 2.2,
                  children: [
                    _buildMetricCard("Today's Sales", '₹${todaySales.toString()}', Icons.payments_outlined, Colors.green),
                    _buildMetricCard("Today's Invoices", todayInvoices.toString(), Icons.receipt_long_outlined, Colors.blue),
                    _buildMetricCard("Total Products", totalProducts.toString(), Icons.inventory_2_outlined, Colors.purple),
                    _buildMetricCard("Total Categories", totalCategories.toString(), Icons.category_outlined, Colors.orange),
                  ],
                );
              },
            ),
            const SizedBox(height: 32),
            // Content Sections: Low Stock & Top Products
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  flex: 1,
                  child: _buildSectionCard(
                    'Low Stock Alerts',
                    Icons.warning_amber_rounded,
                    Colors.amber,
                    lowStockProducts.isEmpty
                        ? const Padding(
                            padding: EdgeInsets.all(16.0),
                            child: Text('All products are sufficiently stocked.', style: TextStyle(color: Colors.grey)),
                          )
                        : Column(
                            children: lowStockProducts.map((p) {
                              return ListTile(
                                leading: CircleAvatar(
                                  backgroundColor: Colors.amber.withOpacity(0.1),
                                  child: const Icon(Icons.inventory_2, color: Colors.amber, size: 20),
                                ),
                                title: Text(p['name'] ?? '', style: const TextStyle(color: Colors.white)),
                                subtitle: Text('Price: ₹${p['sellingPrice']}', style: TextStyle(color: Colors.grey[400])),
                                trailing: Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                                  decoration: BoxDecoration(
                                    color: Colors.red.withOpacity(0.2),
                                    borderRadius: BorderRadius.circular(12),
                                  ),
                                  child: Text(
                                    '${p['stockQuantity']} left',
                                    style: const TextStyle(color: Colors.redAccent, fontWeight: FontWeight.bold, fontSize: 12),
                                  ),
                                ),
                              );
                            }).toList(),
                          ),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  flex: 1,
                  child: _buildSectionCard(
                    'Most Sold Items',
                    Icons.star_outline_rounded,
                    Colors.lightBlueAccent,
                    mostSoldProducts.isEmpty
                        ? const Padding(
                            padding: EdgeInsets.all(16.0),
                            child: Text('No sales records yet.', style: TextStyle(color: Colors.grey)),
                          )
                        : Column(
                            children: mostSoldProducts.map((p) {
                              return ListTile(
                                leading: CircleAvatar(
                                  backgroundColor: Colors.blue.withOpacity(0.1),
                                  child: const Icon(Icons.shopping_bag_outlined, color: Colors.blueAccent, size: 20),
                                ),
                                title: Text(p['productName'] ?? '', style: const TextStyle(color: Colors.white)),
                                subtitle: Text('${p['totalQuantitySold']} units sold', style: TextStyle(color: Colors.grey[400])),
                                trailing: Text(
                                  '₹${p['totalRevenue']}',
                                  style: const TextStyle(color: Colors.greenAccent, fontWeight: FontWeight.bold),
                                ),
                              );
                            }).toList(),
                          ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildMetricCard(String title, String value, IconData icon, Color color) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: Colors.white.withOpacity(0.05)),
      ),
      child: Row(
        children: [
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: color.withOpacity(0.15),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(icon, color: color, size: 28),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(title, style: TextStyle(color: Colors.grey[400], fontSize: 13)),
                const SizedBox(height: 4),
                Text(value, style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.bold)),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSectionCard(String title, IconData icon, Color iconColor, Widget child) {
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
            child: Row(
              children: [
                Icon(icon, color: iconColor),
                const SizedBox(width: 8),
                Text(title, style: const TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold)),
              ],
            ),
          ),
          const Divider(height: 1, color: Color(0xFF334155)),
          child,
        ],
      ),
    );
  }
}
