-- Customer credit invoices. Run once against the same database used by the API.
CREATE TABLE IF NOT EXISTS `CustomerCreditInvoices` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `CustomerName` VARCHAR(150) NOT NULL,
    `CustomerMobileNumber` VARCHAR(20) NOT NULL,
    `BorrowedAmount` DECIMAL(18,2) NOT NULL,
    `OutstandingBalance` DECIMAL(18,2) NOT NULL,
    `InvoiceDate` DATE NOT NULL,
    `IsReceived` TINYINT(1) NOT NULL DEFAULT 0,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ReceivedAt` DATETIME NULL,
    CONSTRAINT `fk_customercreditinvoices_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    INDEX `idx_customercreditinvoices_pending` (`UserId`, `IsReceived`, `InvoiceDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `CustomerCreditTransactions` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `CustomerCreditInvoiceId` INT NOT NULL,
    `TransactionType` ENUM('CREDIT', 'SETTLEMENT') NOT NULL DEFAULT 'CREDIT',
    `Amount` DECIMAL(18,2) NOT NULL,
    `TransactionDate` DATE NOT NULL,
    `ProductName` VARCHAR(150) NULL,
    `Quantity` DECIMAL(18,3) NULL,
    `Price` DECIMAL(18,2) NULL,
    `Notes` VARCHAR(500) NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `IsReceived` TINYINT(1) NOT NULL DEFAULT 0,
    `ReceivedAt` DATETIME NULL,
    CONSTRAINT `fk_customercredittransactions_invoice` FOREIGN KEY (`CustomerCreditInvoiceId`) REFERENCES `CustomerCreditInvoices` (`Id`) ON DELETE CASCADE,
    INDEX `idx_customercredittransactions_invoice_date` (`CustomerCreditInvoiceId`, `TransactionDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
