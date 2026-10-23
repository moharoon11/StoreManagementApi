-- Run this once for an existing database. New installs already receive these
-- columns from schema.sql.
ALTER TABLE `Invoices`
    ADD COLUMN `CustomerName` VARCHAR(150) NULL AFTER `InvoiceNumber`,
    ADD COLUMN `CustomerMobileNumber` VARCHAR(20) NULL AFTER `CustomerName`;
