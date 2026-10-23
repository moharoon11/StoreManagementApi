-- Run once for databases created before per-credit receipts were added.
ALTER TABLE `CustomerCreditTransactions`
    ADD COLUMN `IsReceived` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CreatedAt`,
    ADD COLUMN `ReceivedAt` DATETIME NULL AFTER `IsReceived`;
