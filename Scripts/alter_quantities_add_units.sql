-- Run once for databases created before decimal quantities and product units.
-- Existing quantities and invoices are retained and use Piece as their unit.
ALTER TABLE `Products`
    MODIFY COLUMN `StockQuantity` DECIMAL(18,3) NOT NULL DEFAULT 0.000,
    MODIFY COLUMN `SoldsCount` DECIMAL(18,3) NOT NULL DEFAULT 0.000,
    ADD COLUMN `Unit` VARCHAR(20) NOT NULL DEFAULT 'Piece' AFTER `SoldsCount`;

ALTER TABLE `InvoiceItems`
    MODIFY COLUMN `Quantity` DECIMAL(18,3) NOT NULL,
    ADD COLUMN `Unit` VARCHAR(20) NOT NULL DEFAULT 'Piece' AFTER `Quantity`;

ALTER TABLE `StockMovements`
    MODIFY COLUMN `PreviousQuantity` DECIMAL(18,3) NOT NULL,
    MODIFY COLUMN `QuantityChanged` DECIMAL(18,3) NOT NULL,
    MODIFY COLUMN `NewQuantity` DECIMAL(18,3) NOT NULL;
