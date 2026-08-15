import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class ProductsView extends StatefulWidget {
  const ProductsView({Key? key}) : super(key: key);

  @override
  State<ProductsView> createState() => _ProductsViewState();
}

class _ProductsViewState extends State<ProductsView> {
  bool _isLoading = true;
  List<dynamic> _products = [];
  List<dynamic> _categories = [];
  
  String _searchTerm = '';
  int? _selectedCategoryId;
  bool _showFavouritesOnly = false;

  @override
  void initState() {
    super.initState();
    _loadInitialData();
  }

  Future<void> _loadInitialData() async {
    setState(() => _isLoading = true);
    await _fetchCategories();
    await _fetchProducts();
  }

  Future<void> _fetchCategories() async {
    try {
      final res = await ApiService.get(ApiConfig.categories);
      if (res['success'] == true) {
        _categories = res['data'] ?? [];
      }
    } catch (_) {}
  }

  Future<void> _fetchProducts() async {
    try {
      final queryParams = <String, String>{
        'pageNumber': '1',
        'pageSize': '50',
      };
      if (_searchTerm.isNotEmpty) queryParams['searchTerm'] = _searchTerm;
      if (_selectedCategoryId != null) queryParams['categoryId'] = _selectedCategoryId.toString();
      if (_showFavouritesOnly) queryParams['isFavourite'] = 'true';

      final res = await ApiService.get(ApiConfig.products, queryParameters: queryParams);
      if (res['success'] == true) {
        setState(() {
          _products = res['data']['items'] ?? [];
          _isLoading = false;
        });
      }
    } catch (e) {
      setState(() => _isLoading = false);
    }
  }

  Future<void> _toggleFavourite(int productId, bool isCurrentlyFavourite) async {
    try {
      if (isCurrentlyFavourite) {
        await ApiService.delete('${ApiConfig.favourites}/$productId');
      } else {
        await ApiService.post('${ApiConfig.favourites}/$productId', {});
      }
      _fetchProducts();
    } catch (_) {}
  }

  void _showAddProductModal() {
    final nameController = TextEditingController();
    final costPriceController = TextEditingController();
    final sellingPriceController = TextEditingController();
    final stockController = TextEditingController(text: '10');
    final imageUrlController = TextEditingController();
    final newCatController = TextEditingController();

    int? selectedCategory = _categories.isNotEmpty ? _categories.first['id'] : null;
    bool createNewCategory = false;

    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: const Color(0xFF1E293B),
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (ctx) {
        return StatefulBuilder(
          builder: (context, setModalState) {
            return Padding(
              padding: EdgeInsets.only(
                bottom: MediaQuery.of(context).viewInsets.bottom + 24,
                top: 24,
                left: 24,
                right: 24,
              ),
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text('Add New Product', style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: Colors.white)),
                    const SizedBox(height: 16),
                    TextField(
                      controller: nameController,
                      style: const TextStyle(color: Colors.white),
                      decoration: InputDecoration(
                        labelText: 'Product Name *',
                        labelStyle: TextStyle(color: Colors.grey[400]),
                        filled: true,
                        fillColor: const Color(0xFF0F172A),
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Checkbox(
                          value: createNewCategory,
                          onChanged: (val) {
                            setModalState(() => createNewCategory = val ?? false);
                          },
                          activeColor: Colors.blueAccent,
                        ),
                        const Text('Create a new category inline', style: TextStyle(color: Colors.white)),
                      ],
                    ),
                    if (createNewCategory) ...[
                      TextField(
                        controller: newCatController,
                        style: const TextStyle(color: Colors.white),
                        decoration: InputDecoration(
                          labelText: 'New Category Name *',
                          labelStyle: TextStyle(color: Colors.grey[400]),
                          filled: true,
                          fillColor: const Color(0xFF0F172A),
                          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                      ),
                    ] else ...[
                      DropdownButtonFormField<int>(
                        value: selectedCategory,
                        dropdownColor: const Color(0xFF0F172A),
                        style: const TextStyle(color: Colors.white),
                        decoration: InputDecoration(
                          labelText: 'Select Category',
                          labelStyle: TextStyle(color: Colors.grey[400]),
                          filled: true,
                          fillColor: const Color(0xFF0F172A),
                          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                        items: _categories.map<DropdownMenuItem<int>>((cat) {
                          return DropdownMenuItem<int>(
                            value: cat['id'],
                            child: Text(cat['name'] ?? ''),
                          );
                        }).toList(),
                        onChanged: (val) => setModalState(() => selectedCategory = val),
                      ),
                    ],
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            controller: costPriceController,
                            keyboardType: TextInputType.number,
                            style: const TextStyle(color: Colors.white),
                            decoration: InputDecoration(
                              labelText: 'Cost Price (₹)',
                              labelStyle: TextStyle(color: Colors.grey[400]),
                              filled: true,
                              fillColor: const Color(0xFF0F172A),
                              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            ),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: TextField(
                            controller: sellingPriceController,
                            keyboardType: TextInputType.number,
                            style: const TextStyle(color: Colors.white),
                            decoration: InputDecoration(
                              labelText: 'Selling Price (₹) *',
                              labelStyle: TextStyle(color: Colors.grey[400]),
                              filled: true,
                              fillColor: const Color(0xFF0F172A),
                              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: stockController,
                      keyboardType: TextInputType.number,
                      style: const TextStyle(color: Colors.white),
                      decoration: InputDecoration(
                        labelText: 'Initial Stock Quantity *',
                        labelStyle: TextStyle(color: Colors.grey[400]),
                        filled: true,
                        fillColor: const Color(0xFF0F172A),
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: imageUrlController,
                      style: const TextStyle(color: Colors.white),
                      decoration: InputDecoration(
                        labelText: 'Image URL (Cloudinary)',
                        labelStyle: TextStyle(color: Colors.grey[400]),
                        filled: true,
                        fillColor: const Color(0xFF0F172A),
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                    const SizedBox(height: 24),
                    SizedBox(
                      width: double.infinity,
                      height: 48,
                      child: ElevatedButton(
                        onPressed: () async {
                          if (nameController.text.isEmpty || sellingPriceController.text.isEmpty) return;

                          final Map<String, dynamic> body = {
                            'name': nameController.text.trim(),
                            'costPrice': double.tryParse(costPriceController.text) ?? 0.0,
                            'sellingPrice': double.tryParse(sellingPriceController.text) ?? 0.0,
                            'stockQuantity': int.tryParse(stockController.text) ?? 0,
                            'imageUrl': imageUrlController.text.trim(),
                          };

                          if (createNewCategory) {
                            body['newCategoryName'] = newCatController.text.trim();
                          } else if (selectedCategory != null) {
                            body['categoryId'] = selectedCategory;
                          }

                          Navigator.pop(ctx);
                          await ApiService.post(ApiConfig.products, body);
                          await _fetchCategories();
                          await _fetchProducts();
                        },
                        style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
                        child: const Text('Create Product', style: TextStyle(fontWeight: FontWeight.bold)),
                      ),
                    ),
                  ],
                ),
              ),
            );
          },
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(24.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Text('Products Catalog', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white)),
              const Spacer(),
              ElevatedButton.icon(
                onPressed: _showAddProductModal,
                icon: const Icon(Icons.add),
                label: const Text('Add Product'),
                style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
              ),
            ],
          ),
          const SizedBox(height: 16),
          // Filter Bar
          Row(
            children: [
              Expanded(
                child: TextField(
                  style: const TextStyle(color: Colors.white),
                  onChanged: (val) {
                    _searchTerm = val;
                    _fetchProducts();
                  },
                  decoration: InputDecoration(
                    hintText: 'Search products by name or category...',
                    hintStyle: TextStyle(color: Colors.grey[400]),
                    prefixIcon: const Icon(Icons.search, color: Colors.blueAccent),
                    filled: true,
                    fillColor: const Color(0xFF1E293B),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              FilterChip(
                label: const Text('Favourites'),
                selected: _showFavouritesOnly,
                selectedColor: Colors.amber.withOpacity(0.2),
                checkmarkColor: Colors.amber,
                onSelected: (selected) {
                  setState(() => _showFavouritesOnly = selected);
                  _fetchProducts();
                },
              ),
            ],
          ),
          const SizedBox(height: 24),
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator(color: Colors.blueAccent))
                : _products.isEmpty
                    ? const Center(child: Text('No products found.', style: TextStyle(color: Colors.grey)))
                    : GridView.builder(
                        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                          maxCrossAxisExtent: 260,
                          childAspectRatio: 0.75,
                          crossAxisSpacing: 16,
                          mainAxisSpacing: 16,
                        ),
                        itemCount: _products.length,
                        itemBuilder: (context, index) {
                          final p = _products[index];
                          final isFav = p['isFavourite'] == 1 || p['isFavourite'] == true;

                          return Container(
                            decoration: BoxDecoration(
                              color: const Color(0xFF1E293B),
                              borderRadius: BorderRadius.circular(16),
                              border: Border.all(color: Colors.white.withOpacity(0.05)),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Stack(
                                  children: [
                                    Container(
                                      height: 130,
                                      width: double.infinity,
                                      decoration: BoxDecoration(
                                        borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
                                        color: const Color(0xFF0F172A),
                                        image: p['imageUrl'] != null && (p['imageUrl'] as String).isNotEmpty
                                            ? DecorationImage(image: NetworkImage(p['imageUrl']), fit: BoxFit.cover)
                                            : null,
                                      ),
                                      child: p['imageUrl'] == null || (p['imageUrl'] as String).isEmpty
                                          ? const Icon(Icons.inventory, color: Colors.grey, size: 40)
                                          : null,
                                    ),
                                    Positioned(
                                      top: 8,
                                      right: 8,
                                      child: IconButton(
                                        icon: Icon(isFav ? Icons.star : Icons.star_border, color: Colors.amber),
                                        onPressed: () => _toggleFavourite(p['id'], isFav),
                                      ),
                                    ),
                                  ],
                                ),
                                Padding(
                                  padding: const EdgeInsets.all(12.0),
                                  child: Column(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        p['categoryName'] ?? '',
                                        style: const TextStyle(color: Colors.blueAccent, fontSize: 11, fontWeight: FontWeight.bold),
                                      ),
                                      const SizedBox(height: 2),
                                      Text(
                                        p['name'] ?? '',
                                        maxLines: 1,
                                        overflow: TextOverflow.ellipsis,
                                        style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15),
                                      ),
                                      const SizedBox(height: 8),
                                      Row(
                                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                        children: [
                                          Text(
                                            '₹${p['sellingPrice']}',
                                            style: const TextStyle(color: Colors.greenAccent, fontSize: 16, fontWeight: FontWeight.bold),
                                          ),
                                          Container(
                                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                                            decoration: BoxDecoration(
                                              color: (p['stockQuantity'] as int) <= 5 ? Colors.red.withOpacity(0.2) : Colors.green.withOpacity(0.2),
                                              borderRadius: BorderRadius.circular(8),
                                            ),
                                            child: Text(
                                              'Qty: ${p['stockQuantity']}',
                                              style: TextStyle(
                                                color: (p['stockQuantity'] as int) <= 5 ? Colors.redAccent : Colors.greenAccent,
                                                fontSize: 12,
                                              ),
                                            ),
                                          ),
                                        ],
                                      ),
                                    ],
                                  ),
                                ),
                              ],
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
