-- Migration to support bill uploads in stock movements
ALTER TABLE `StockMovements` 
MODIFY COLUMN `Reason` ENUM('SALE', 'STOCK_ADDED', 'MANUAL_ADJUSTMENT', 'RETURN', 'BILL_UPLOAD') NOT NULL;
