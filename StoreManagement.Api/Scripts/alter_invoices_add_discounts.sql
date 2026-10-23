-- Run once on existing MySQL databases before deploying the discount-enabled API.
ALTER TABLE `Invoices`
    ADD COLUMN `DiscountType` VARCHAR(20) NULL AFTER `Subtotal`,
    ADD COLUMN `DiscountValue` DECIMAL(18,2) NOT NULL DEFAULT 0.00 AFTER `DiscountType`,
    ADD COLUMN `DiscountAmount` DECIMAL(18,2) NOT NULL DEFAULT 0.00 AFTER `DiscountValue`;

-- Existing invoices deliberately retain a zero discount. Their historic totals
-- and payment records are therefore unchanged.
