import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../providers/app_provider.dart';
import '../views/dashboard/dashboard_view.dart';
import '../views/products/products_view.dart';
import '../views/billing/pos_checkout_view.dart';
import '../views/categories/categories_view.dart';
import '../views/invoices/invoice_history_view.dart';
import '../views/stock/stock_management_view.dart';
import '../views/reports/sales_reports_view.dart';
import '../views/store/store_profile_view.dart';

class ResponsiveLayout extends StatelessWidget {
  const ResponsiveLayout({Key? key}) : super(key: key);

  final List<Widget> _views = const [
    DashboardView(),
    PosCheckoutView(),
    ProductsView(),
    CategoriesView(),
    InvoiceHistoryView(),
    StockManagementView(),
    SalesReportsView(),
    StoreProfileView(),
  ];

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<AppProvider>(context);
    final isDesktop = MediaQuery.of(context).size.width >= 850;

    final navItems = [
      const NavigationRailDestination(icon: Icon(Icons.dashboard_outlined), selectedIcon: Icon(Icons.dashboard), label: Text('Dashboard')),
      const NavigationRailDestination(icon: Icon(Icons.point_of_sale_outlined), selectedIcon: Icon(Icons.point_of_sale), label: Text('POS Billing')),
      const NavigationRailDestination(icon: Icon(Icons.inventory_2_outlined), selectedIcon: Icon(Icons.inventory_2), label: Text('Products')),
      const NavigationRailDestination(icon: Icon(Icons.category_outlined), selectedIcon: Icon(Icons.category), label: Text('Categories')),
      const NavigationRailDestination(icon: Icon(Icons.receipt_long_outlined), selectedIcon: Icon(Icons.receipt_long), label: Text('Invoices')),
      const NavigationRailDestination(icon: Icon(Icons.inventory_outlined), selectedIcon: Icon(Icons.inventory), label: Text('Stock')),
      const NavigationRailDestination(icon: Icon(Icons.analytics_outlined), selectedIcon: Icon(Icons.analytics), label: Text('Reports')),
      const NavigationRailDestination(icon: Icon(Icons.store_outlined), selectedIcon: Icon(Icons.store), label: Text('Store Profile')),
    ];

    if (isDesktop) {
      return Scaffold(
        backgroundColor: const Color(0xFF0F172A),
        body: Row(
          children: [
            NavigationRail(
              backgroundColor: const Color(0xFF1E293B),
              selectedIndex: provider.selectedNavIndex,
              onDestinationSelected: (int index) {
                provider.setNavIndex(index);
              },
              extended: MediaQuery.of(context).size.width >= 1100,
              minExtendedWidth: 200,
              leading: Padding(
                padding: const EdgeInsets.symmetric(vertical: 20.0),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.storefront, color: Colors.blueAccent, size: 28),
                    if (MediaQuery.of(context).size.width >= 1100) ...[
                      const SizedBox(width: 8),
                      const Text('POS System', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 18)),
                    ],
                  ],
                ),
              ),
              trailing: Expanded(
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: Padding(
                    padding: const EdgeInsets.only(bottom: 20.0),
                    child: IconButton(
                      icon: const Icon(Icons.logout, color: Colors.redAccent),
                      onPressed: provider.logout,
                      tooltip: 'Logout',
                    ),
                  ),
                ),
              ),
              selectedIconTheme: const IconThemeData(color: Colors.blueAccent),
              unselectedIconTheme: const IconThemeData(color: Colors.grey),
              selectedLabelTextStyle: const TextStyle(color: Colors.blueAccent, fontWeight: FontWeight.bold),
              unselectedLabelTextStyle: const TextStyle(color: Colors.grey),
              destinations: navItems,
            ),
            const VerticalDivider(thickness: 1, width: 1, color: Color(0xFF334155)),
            Expanded(
              child: _views[provider.selectedNavIndex],
            ),
          ],
        ),
      );
    } else {
      // Mobile / Tablet Drawer Layout
      return Scaffold(
        backgroundColor: const Color(0xFF0F172A),
        appBar: AppBar(
          backgroundColor: const Color(0xFF1E293B),
          elevation: 0,
          title: const Text('Store Management POS', style: TextStyle(color: Colors.white)),
          actions: [
            IconButton(
              icon: const Icon(Icons.logout, color: Colors.redAccent),
              onPressed: provider.logout,
            ),
          ],
        ),
        drawer: Drawer(
          backgroundColor: const Color(0xFF1E293B),
          child: ListView(
            padding: EdgeInsets.zero,
            children: [
              DrawerHeader(
                decoration: const BoxDecoration(color: Color(0xFF0F172A)),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    const Icon(Icons.storefront, color: Colors.blueAccent, size: 40),
                    const SizedBox(height: 12),
                    Text('User: ${provider.username}', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 16)),
                  ],
                ),
              ),
              _buildDrawerItem(context, provider, 0, 'Dashboard', Icons.dashboard),
              _buildDrawerItem(context, provider, 1, 'POS Billing', Icons.point_of_sale),
              _buildDrawerItem(context, provider, 2, 'Products', Icons.inventory_2),
              _buildDrawerItem(context, provider, 3, 'Categories', Icons.category),
              _buildDrawerItem(context, provider, 4, 'Invoices', Icons.receipt_long),
              _buildDrawerItem(context, provider, 5, 'Stock Management', Icons.inventory),
              _buildDrawerItem(context, provider, 6, 'Sales Reports', Icons.analytics),
              _buildDrawerItem(context, provider, 7, 'Store Profile', Icons.store),
            ],
          ),
        ),
        body: _views[provider.selectedNavIndex],
      );
    }
  }

  Widget _buildDrawerItem(BuildContext context, AppProvider provider, int index, String title, IconData icon) {
    final isSelected = provider.selectedNavIndex == index;
    return ListTile(
      leading: Icon(icon, color: isSelected ? Colors.blueAccent : Colors.grey),
      title: Text(title, style: TextStyle(color: isSelected ? Colors.blueAccent : Colors.white)),
      selected: isSelected,
      onTap: () {
        provider.setNavIndex(index);
        Navigator.pop(context);
      },
    );
  }
}
