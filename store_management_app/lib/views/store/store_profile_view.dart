import 'package:flutter/material.dart';
import '../../services/api_service.dart';
import '../../config/api_config.dart';

class StoreProfileView extends StatefulWidget {
  const StoreProfileView({Key? key}) : super(key: key);

  @override
  State<StoreProfileView> createState() => _StoreProfileViewState();
}

class _StoreProfileViewState extends State<StoreProfileView> {
  bool _isLoading = true;
  bool _isSaving = false;

  final _storeNameController = TextEditingController();
  final _ownerNameController = TextEditingController();
  final _addressController = TextEditingController();
  final _cityController = TextEditingController();
  final _districtController = TextEditingController();
  final _pincodeController = TextEditingController();
  final _emailController = TextEditingController();
  final _phoneController = TextEditingController();
  final _gstController = TextEditingController();
  final _logoUrlController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _loadProfile();
  }

  @override
  void dispose() {
    _storeNameController.dispose();
    _ownerNameController.dispose();
    _addressController.dispose();
    _cityController.dispose();
    _districtController.dispose();
    _pincodeController.dispose();
    _emailController.dispose();
    _phoneController.dispose();
    _gstController.dispose();
    _logoUrlController.dispose();
    super.dispose();
  }

  Future<void> _loadProfile() async {
    setState(() => _isLoading = true);
    try {
      final res = await ApiService.get(ApiConfig.storeProfile);
      if (res['success'] == true && res['data'] != null) {
        final data = res['data'];
        _storeNameController.text = data['storeName'] ?? '';
        _ownerNameController.text = data['ownerName'] ?? '';
        _addressController.text = data['address'] ?? '';
        _cityController.text = data['city'] ?? '';
        _districtController.text = data['district'] ?? '';
        _pincodeController.text = data['pincode'] ?? '';
        _emailController.text = data['email'] ?? '';
        _phoneController.text = data['phone'] ?? '';
        _gstController.text = data['gstNumber'] ?? '';
        _logoUrlController.text = data['logoUrl'] ?? '';
      }
    } catch (e) {
      // Profile not created yet
    }
    setState(() => _isLoading = false);
  }

  Future<void> _saveProfile() async {
    if (_storeNameController.text.isEmpty || _ownerNameController.text.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Store Name and Owner Name are required.')),
      );
      return;
    }

    setState(() => _isSaving = true);
    try {
      final res = await ApiService.post(ApiConfig.storeProfile, {
        'storeName': _storeNameController.text,
        'ownerName': _ownerNameController.text,
        'address': _addressController.text,
        'city': _cityController.text,
        'district': _districtController.text,
        'pincode': _pincodeController.text,
        'email': _emailController.text,
        'phone': _phoneController.text,
        'gstNumber': _gstController.text,
        'existingLogoUrl': _logoUrlController.text,
      });

      if (res['success'] == true && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Store Profile saved successfully!')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Error: ${e.toString()}')),
        );
      }
    }
    setState(() => _isSaving = false);
  }

  @override
  Widget build(BuildContext context) {
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator(color: Colors.blueAccent));
    }

    return SingleChildScrollView(
      padding: const EdgeInsets.all(24.0),
      child: Center(
        child: Container(
          constraints: const BoxConstraints(maxWidth: 800),
          padding: const EdgeInsets.all(24),
          decoration: BoxDecoration(
            color: const Color(0xFF1E293B),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: Colors.white.withOpacity(0.05)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Store & Business Profile',
                style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold, color: Colors.white),
              ),
              const SizedBox(height: 4),
              const Text(
                'This information appears on generated invoice PDFs',
                style: TextStyle(color: Colors.grey),
              ),
              const SizedBox(height: 24),
              Row(
                children: [
                  Expanded(child: _buildTextField('Store Name *', _storeNameController, Icons.store)),
                  const SizedBox(width: 16),
                  Expanded(child: _buildTextField('Owner Name *', _ownerNameController, Icons.person)),
                ],
              ),
              const SizedBox(height: 16),
              Row(
                children: [
                  Expanded(child: _buildTextField('GSTIN Number', _gstController, Icons.assignment_outlined)),
                  const SizedBox(width: 16),
                  Expanded(child: _buildTextField('Phone Number', _phoneController, Icons.phone)),
                ],
              ),
              const SizedBox(height: 16),
              Row(
                children: [
                  Expanded(child: _buildTextField('Email Address', _emailController, Icons.email)),
                  const SizedBox(width: 16),
                  Expanded(child: _buildTextField('Logo URL (Cloudinary)', _logoUrlController, Icons.image)),
                ],
              ),
              const SizedBox(height: 16),
              _buildTextField('Address', _addressController, Icons.location_on, maxLines: 2),
              const SizedBox(height: 16),
              Row(
                children: [
                  Expanded(child: _buildTextField('City', _cityController, Icons.location_city)),
                  const SizedBox(width: 16),
                  Expanded(child: _buildTextField('District', _districtController, Icons.map)),
                  const SizedBox(width: 16),
                  Expanded(child: _buildTextField('Pincode', _pincodeController, Icons.pin_drop)),
                ],
              ),
              const SizedBox(height: 32),
              SizedBox(
                width: double.infinity,
                height: 48,
                child: ElevatedButton(
                  onPressed: _isSaving ? null : _saveProfile,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: Colors.blueAccent,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                  child: _isSaving
                      ? const CircularProgressIndicator(color: Colors.white)
                      : const Text('Save Store Profile', style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold)),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildTextField(String label, TextEditingController controller, IconData icon, {int maxLines = 1}) {
    return TextField(
      controller: controller,
      maxLines: maxLines,
      style: const TextStyle(color: Colors.white),
      decoration: InputDecoration(
        labelText: label,
        labelStyle: TextStyle(color: Colors.grey[400]),
        prefixIcon: Icon(icon, color: Colors.blueAccent),
        filled: true,
        fillColor: const Color(0xFF0F172A),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide.none,
        ),
      ),
    );
  }
}
