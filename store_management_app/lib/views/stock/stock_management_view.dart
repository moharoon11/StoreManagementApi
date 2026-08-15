import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class StockManagementView extends StatefulWidget {
  const StockManagementView({Key? key}) : super(key: key);

  @override
  State<StockManagementView> createState() => _StockManagementViewState();
}

class _StockManagementViewState extends State<StockManagementView> {
  bool _isLoading = true;
  List<dynamic> _movements = [];
  List<dynamic> _products = [];

  @override
  void initState() {
    super.initState();
    _loadData();
  }

  Future<void> _loadData() async {
    setState(() => _isLoading = true);
    await _fetchProducts();
    await _fetchMovements();
  }

  Future<void> _fetchProducts() async {
    try {
      final res = await ApiService.get(ApiConfig.products, queryParameters: {'pageSize': '100'});
      if (res['success'] == true) {
        _products = res['data']['items'] ?? [];
      }
    } catch (_) {}
  }

  Future<void> _fetchMovements() async {
    try {
      final res = await ApiService.get(ApiConfig.stockMovements);
      if (res['success'] == true) {
        setState(() {
          _movements = res['data'] ?? [];
          _isLoading = false;
        });
      }
    } catch (_) {
      setState(() => _isLoading = false);
    }
  }

  void _showAdjustStockDialog() {
    if (_products.isEmpty) return;

    int selectedProductId = _products.first['id'];
    final qtyController = TextEditingController();
    String reason = 'STOCK_ADDED';

    showDialog(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (context, setModalState) {
          return AlertDialog(
            backgroundColor: const Color(0xFF1E293B),
            title: const Text('Adjust Product Stock', style: TextStyle(color: Colors.white)),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<int>(
                  value: selectedProductId,
                  dropdownColor: const Color(0xFF0F172A),
                  style: const TextStyle(color: Colors.white),
                  decoration: InputDecoration(
                    labelText: 'Select Product',
                    labelStyle: TextStyle(color: Colors.grey[400]),
                    filled: true,
                    fillColor: const Color(0xFF0F172A),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                  items: _products.map<DropdownMenuItem<int>>((p) {
                    return DropdownMenuItem<int>(
                      value: p['id'],
                      child: Text('${p['name']} (Stock: ${p['stockQuantity']})'),
                    );
                  }).toList(),
                  onChanged: (val) => setModalState(() => selectedProductId = val!),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: qtyController,
                  keyboardType: TextInputType.number,
                  style: const TextStyle(color: Colors.white),
                  decoration: InputDecoration(
                    labelText: 'Quantity Change (+ to add, - to reduce)',
                    labelStyle: TextStyle(color: Colors.grey[400]),
                    filled: true,
                    fillColor: const Color(0xFF0F172A),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  value: reason,
                  dropdownColor: const Color(0xFF0F172A),
                  style: const TextStyle(color: Colors.white),
                  decoration: InputDecoration(
                    labelText: 'Reason',
                    labelStyle: TextStyle(color: Colors.grey[400]),
                    filled: true,
                    fillColor: const Color(0xFF0F172A),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                  items: const [
                    DropdownMenuItem(value: 'STOCK_ADDED', child: Text('STOCK_ADDED')),
                    DropdownMenuItem(value: 'MANUAL_ADJUSTMENT', child: Text('MANUAL_ADJUSTMENT')),
                    DropdownMenuItem(value: 'RETURN', child: Text('RETURN')),
                  ],
                  onChanged: (val) => setModalState(() => reason = val!),
                ),
              ],
            ),
            actions: [
              TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
              ElevatedButton(
                onPressed: () async {
                  final qty = int.tryParse(qtyController.text) ?? 0;
                  if (qty == 0) return;

                  Navigator.pop(ctx);
                  await ApiService.post(ApiConfig.stockAdjust, {
                    'productId': selectedProductId,
                    'quantityChanged': qty,
                    'reason': reason,
                  });
                  _loadData();
                },
                style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
                child: const Text('Submit Adjustment'),
              ),
            ],
          );
        },
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
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text('Stock Audit & Movements', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white)),
              ElevatedButton.icon(
                onPressed: _showAdjustStockDialog,
                icon: const Icon(Icons.edit_note),
                label: const Text('Adjust Stock'),
                style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
              ),
            ],
          ),
          const SizedBox(height: 24),
          Expanded(
            child: _movements.isEmpty
                ? const Center(child: Text('No stock movement records found.', style: TextStyle(color: Colors.grey)))
                : ListView.builder(
                    itemCount: _movements.length,
                    itemBuilder: (context, index) {
                      final m = _movements[index];
                      final isAddition = (m['quantityChanged'] as int) > 0;

                      return Card(
                        color: const Color(0xFF1E293B),
                        margin: const EdgeInsets.only(bottom: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        child: ListTile(
                          leading: CircleAvatar(
                            backgroundColor: isAddition ? Colors.green.withOpacity(0.1) : Colors.red.withOpacity(0.1),
                            child: Icon(
                              isAddition ? Icons.add : Icons.remove,
                              color: isAddition ? Colors.greenAccent : Colors.redAccent,
                            ),
                          ),
                          title: Text(m['productName'] ?? '', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                          subtitle: Text('Prev: ${m['previousQuantity']} → New: ${m['newQuantity']}  (${m['reason']})', style: const TextStyle(color: Colors.grey)),
                          trailing: Text(
                            '${isAddition ? '+' : ''}${m['quantityChanged']}',
                            style: TextStyle(
                              color: isAddition ? Colors.greenAccent : Colors.redAccent,
                              fontSize: 18,
                              fontWeight: FontWeight.bold,
                            ),
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
