import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class CategoriesView extends StatefulWidget {
  const CategoriesView({Key? key}) : super(key: key);

  @override
  State<CategoriesView> createState() => _CategoriesViewState();
}

class _CategoriesViewState extends State<CategoriesView> {
  bool _isLoading = true;
  List<dynamic> _categories = [];

  @override
  void initState() {
    super.initState();
    _fetchCategories();
  }

  Future<void> _fetchCategories() async {
    setState(() => _isLoading = true);
    try {
      final res = await ApiService.get(ApiConfig.categories);
      if (res['success'] == true) {
        setState(() {
          _categories = res['data'] ?? [];
          _isLoading = false;
        });
      }
    } catch (e) {
      setState(() => _isLoading = false);
    }
  }

  void _showCategoryDialog({Map<String, dynamic>? category}) {
    final nameController = TextEditingController(text: category?['name'] ?? '');
    final imageUrlController = TextEditingController(text: category?['imageUrl'] ?? '');

    showDialog(
      context: context,
      builder: (ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E293B),
          title: Text(
            category == null ? 'Add Category' : 'Edit Category',
            style: const TextStyle(color: Colors.white),
          ),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: nameController,
                style: const TextStyle(color: Colors.white),
                decoration: InputDecoration(
                  labelText: 'Category Name',
                  labelStyle: TextStyle(color: Colors.grey[400]),
                  filled: true,
                  fillColor: const Color(0xFF0F172A),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                ),
              ),
              const SizedBox(height: 16),
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
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('Cancel', style: TextStyle(color: Colors.grey)),
            ),
            ElevatedButton(
              onPressed: () async {
                final name = nameController.text.trim();
                if (name.isEmpty) return;

                Navigator.pop(ctx);
                if (category == null) {
                  await ApiService.post(ApiConfig.categories, {
                    'name': name,
                    'imageUrl': imageUrlController.text.trim(),
                  });
                } else {
                  await ApiService.put('${ApiConfig.categories}/${category['id']}', {
                    'name': name,
                    'imageUrl': imageUrlController.text.trim(),
                  });
                }
                _fetchCategories();
              },
              style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
              child: const Text('Save'),
            ),
          ],
        );
      },
    );
  }

  Future<void> _deleteCategory(int id) async {
    try {
      await ApiService.delete('${ApiConfig.categories}/$id');
      _fetchCategories();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.toString().replaceAll('Exception: ', ''))),
        );
      }
    }
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
              const Text(
                'Product Categories',
                style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white),
              ),
              ElevatedButton.icon(
                onPressed: () => _showCategoryDialog(),
                icon: const Icon(Icons.add),
                label: const Text('Add Category'),
                style: ElevatedButton.styleFrom(backgroundColor: Colors.blueAccent),
              ),
            ],
          ),
          const SizedBox(height: 24),
          Expanded(
            child: _categories.isEmpty
                ? const Center(child: Text('No categories added yet.', style: TextStyle(color: Colors.grey)))
                : GridView.builder(
                    gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                      maxCrossAxisExtent: 240,
                      childAspectRatio: 1.1,
                      crossAxisSpacing: 16,
                      mainAxisSpacing: 16,
                    ),
                    itemCount: _categories.length,
                    itemBuilder: (context, index) {
                      final cat = _categories[index];
                      return Container(
                        padding: const EdgeInsets.all(16),
                        decoration: BoxDecoration(
                          color: const Color(0xFF1E293B),
                          borderRadius: BorderRadius.circular(16),
                          border: Border.all(color: Colors.white.withOpacity(0.05)),
                        ),
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            CircleAvatar(
                              radius: 30,
                              backgroundColor: Colors.blue.withOpacity(0.1),
                              backgroundImage: cat['imageUrl'] != null && (cat['imageUrl'] as String).isNotEmpty
                                  ? NetworkImage(cat['imageUrl'])
                                  : null,
                              child: cat['imageUrl'] == null || (cat['imageUrl'] as String).isEmpty
                                  ? const Icon(Icons.category, color: Colors.blueAccent, size: 30)
                                  : null,
                            ),
                            const SizedBox(height: 12),
                            Text(
                              cat['name'] ?? '',
                              textAlign: TextAlign.center,
                              style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 16),
                            ),
                            const SizedBox(height: 8),
                            Row(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: [
                                IconButton(
                                  icon: const Icon(Icons.edit, color: Colors.blueAccent, size: 20),
                                  onPressed: () => _showCategoryDialog(category: cat),
                                ),
                                IconButton(
                                  icon: const Icon(Icons.delete_outline, color: Colors.redAccent, size: 20),
                                  onPressed: () => _deleteCategory(cat['id']),
                                ),
                              ],
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
