-- Run once for an existing database before deploying the selectable invoice
-- date API support. Existing invoices retain the date they were created.
ALTER TABLE `Invoices`
    ADD COLUMN `InvoiceDate` DATE DEFAULT NULL AFTER `BalanceDue`;

UPDATE `Invoices`
SET `InvoiceDate` = DATE(`CreatedAt`)
WHERE `InvoiceDate` IS NULL;

CREATE INDEX `idx_invoices_user_invoice_date`
    ON `Invoices` (`UserId`, `InvoiceDate`);
