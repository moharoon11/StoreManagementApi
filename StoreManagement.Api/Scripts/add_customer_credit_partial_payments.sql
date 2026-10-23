-- Run once before deploying partial-payment support for customer credit.
ALTER TABLE `CustomerCreditTransactions`
    ADD COLUMN `ReceivedAmount` DECIMAL(18,2) NOT NULL DEFAULT 0.00 AFTER `Amount`;

-- Existing entries already marked received are fully settled.
UPDATE `CustomerCreditTransactions`
SET `ReceivedAmount` = `Amount`
WHERE `TransactionType` = 'CREDIT' AND `IsReceived` = 1;
