import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/app_provider.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class PosCheckoutView extends StatefulWidget {
  const PosCheckoutView({Key? key}) : super(key: key);

  @override
  State<PosCheckoutView> createState() => _PosCheckoutViewState();
}

class _PosCheckoutViewState extends State<PosCheckoutView> {
  bool _isLoading = true;
  bool _isCheckingOut = false;
  List<dynamic> _products = [];
  String _searchTerm = '';

  @override
  void initState() {
    super.initState();
    _fetchAvailableProducts();
  }

  Future<void> _fetchAvailableProducts() async {
    setState(() => _isLoading = true);
    try {
      final res = await ApiService.get(ApiConfig.products, queryParameters: {'pageSize': '100'});
      if (res['success'] == true) {
        setState(() {
          _products = res['data']['items'] ?? [];
          _isLoading = false;
        });
      }
    } catch (_) {
      setState(() => _isLoading = false);
    }
  }

  Future<void> _processCheckout(AppProvider provider) async {
    if (provider.cartItems.isEmpty) return;

    setState(() => _isCheckingOut = true);
    try {
      final items = provider.cartItems.values.map((item) {
        return {
          'productId': item['product']['id'],
          'quantity': item['quantity'],
        };
      }).toList();

      final res = await ApiService.post(ApiConfig.checkout, {'items': items});

      if (res['success'] == true && mounted) {
        final invoice = res['data'];
        provider.clearCart();
        _fetchAvailableProducts();

        showDialog(
          context: context,
          builder: (ctx) => AlertDialog(
            backgroundColor: const Color(0xFF1E293B),
            title: const Row(
              children: [
                Icon(Icons.check_circle, color: Colors.greenAccent),
                SizedBox(width: 8),
                Text('Checkout Completed', style: TextStyle(color: Colors.white)),
              ],
            ),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Invoice #: ${invoice['invoiceNumber']}', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                const SizedBox(height: 8),
                Text('Grand Total: ₹${invoice['grandTotal']}', style: const TextStyle(color: Colors.greenAccent, fontSize: 18, fontWeight: FontWeight.bold)),
                const SizedBox(height: 8),
                Text('${invoice['items']?.length ?? 0} item types processed.', style: const TextStyle(color: Colors.grey)),
              ],
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('Close'),
              ),
            ],
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Checkout Error: ${e.toString().replaceAll('Exception: ', '')}')),
        );
      }
    }
    setState(() => _isCheckingOut = false);
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<AppProvider>(context);

    final filteredProducts = _products.where((p) {
      final name = (p['name'] as String).toLowerCase();
      final cat = (p['categoryName'] as String).toLowerCase();
      final search = _searchTerm.toLowerCase();
      return name.contains(search) || cat.contains(search);
    }).toList();

    return LayoutBuilder(
      builder: (context, constraints) {
        bool isWide = constraints.maxWidth > 800;

        Widget productListSection = Column(
          children: [
            TextField(
              style: const TextStyle(color: Colors.white),
              onChanged: (val) => setState(() => _searchTerm = val),
              decoration: InputDecoration(
                hintText: 'Search product to add to bill...',
                hintStyle: TextStyle(color: Colors.grey[400]),
                prefixIcon: const Icon(Icons.search, color: Colors.blueAccent),
                filled: true,
                fillColor: const Color(0xFF1E293B),
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
              ),
            ),
            const SizedBox(height: 16),
            Expanded(
              child: _isLoading
                  ? const Center(child: CircularProgressIndicator(color: Colors.blueAccent))
                  : GridView.builder(
                      gridDelegate: SliverGridDelegateWithMaxCrossAxisExtent(
                        maxCrossAxisExtent: isWide ? 200 : 160,
                        childAspectRatio: 0.85,
                        crossAxisSpacing: 12,
                        mainAxisSpacing: 12,
                      ),
                      itemCount: filteredProducts.length,
                      itemBuilder: (context, index) {
                        final p = filteredProducts[index];
                        final stock = p['stockQuantity'] as int;

                        return InkWell(
                          onTap: stock > 0 ? () => provider.addToCart(p) : null,
                          borderRadius: BorderRadius.circular(12),
                          child: Container(
                            padding: const EdgeInsets.all(12),
                            decoration: BoxDecoration(
                              color: const Color(0xFF1E293B),
                              borderRadius: BorderRadius.circular(12),
                              border: Border.all(
                                color: provider.cartItems.containsKey(p['id']) ? Colors.blueAccent : Colors.white.withOpacity(0.05),
                                width: provider.cartItems.containsKey(p['id']) ? 2 : 1,
                              ),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  p['name'] ?? '',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13),
                                ),
                                const Spacer(),
                                Text('₹${p['sellingPrice']}', style: const TextStyle(color: Colors.greenAccent, fontWeight: FontWeight.bold)),
                                const SizedBox(height: 4),
                                Row(
                                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                  children: [
                                    Text('Stock: $stock', style: TextStyle(color: stock > 0 ? Colors.grey[400] : Colors.redAccent, fontSize: 11)),
                                    const Icon(Icons.add_shopping_cart, color: Colors.blueAccent, size: 18),
                                  ],
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
            ),
          ],
        );

        Widget cartSection = Container(
          padding: const EdgeInsets.all(20),
          decoration: BoxDecoration(
            color: const Color(0xFF1E293B),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: Colors.white.withOpacity(0.05)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text('Current Invoice / Cart', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 18)),
                  IconButton(
                    icon: const Icon(Icons.delete_sweep, color: Colors.redAccent),
                    onPressed: provider.clearCart,
                    tooltip: 'Clear Cart',
                  ),
                ],
              ),
              const Divider(color: Color(0xFF334155)),
              Expanded(
                child: provider.cartItems.isEmpty
                    ? const Center(child: Text('Tap products on left to add to bill.', style: TextStyle(color: Colors.grey)))
                    : ListView(
                        children: provider.cartItems.values.map((item) {
                          final product = item['product'];
                          final qty = item['quantity'] as int;
                          final price = (product['sellingPrice'] as num).toDouble();

                          return ListTile(
                            contentPadding: EdgeInsets.zero,
                            title: Text(product['name'] ?? '', style: const TextStyle(color: Colors.white, fontSize: 14)),
                            subtitle: Text('₹$price × $qty = ₹${price * qty}', style: const TextStyle(color: Colors.greenAccent)),
                            trailing: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                IconButton(
                                  icon: const Icon(Icons.remove_circle_outline, color: Colors.amber, size: 20),
                                  onPressed: () => provider.removeFromCart(product['id']),
                                ),
                                Text('$qty', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                                IconButton(
                                  icon: const Icon(Icons.add_circle_outline, color: Colors.blueAccent, size: 20),
                                  onPressed: () => provider.addToCart(product),
                                ),
                              ],
                            ),
                          );
                        }).toList(),
                      ),
              ),
              const Divider(color: Color(0xFF334155)),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text('Grand Total:', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.bold)),
                  Text('₹${provider.cartTotal.toStringAsFixed(2)}', style: const TextStyle(color: Colors.greenAccent, fontSize: 22, fontWeight: FontWeight.bold)),
                ],
              ),
              const SizedBox(height: 16),
              SizedBox(
                width: double.infinity,
                height: 50,
                child: ElevatedButton.icon(
                  onPressed: provider.cartItems.isEmpty || _isCheckingOut ? null : () => _processCheckout(provider),
                  icon: const Icon(Icons.point_of_sale),
                  label: _isCheckingOut
                      ? const CircularProgressIndicator(color: Colors.white)
                      : Text('Checkout (${provider.cartCount} items)', style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
                  style: ElevatedButton.styleFrom(
                    backgroundColor: Colors.green,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                ),
              ),
            ],
          ),
        );

        if (isWide) {
          return Padding(
            padding: const EdgeInsets.all(24.0),
            child: Row(
              children: [
                Expanded(flex: 3, child: productListSection),
                const SizedBox(width: 24),
                Expanded(flex: 2, child: cartSection),
              ],
            ),
          );
        } else {
          return Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              children: [
                Expanded(flex: 3, child: productListSection),
                const SizedBox(height: 16),
                Expanded(flex: 2, child: cartSection),
              ],
            ),
          );
        }
      },
    );
  }
}
