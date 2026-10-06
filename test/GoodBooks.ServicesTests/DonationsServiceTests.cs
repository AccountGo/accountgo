using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Donations;
using Core.Domain.Financials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Donations;
using DonationDto = Dto.Donations.DonationInvoice;

namespace GoodBooks.ServicesTests
{
    public class DonationsServiceTests
    {
        private readonly Mock<IRepository<DonationInvoiceHeader>> donations = new Mock<IRepository<DonationInvoiceHeader>>();
        private readonly Mock<IRepository<DonationInvoiceLine>> lines = new Mock<IRepository<DonationInvoiceLine>>();
        private readonly Mock<IRepository<Donor>> donors = new Mock<IRepository<Donor>>();
        private readonly Mock<IRepository<Account>> accounts = new Mock<IRepository<Account>>();
        private readonly DonationsService service;

        public DonationsServiceTests()
        {
            var sequences = new Mock<IRepository<SequenceNumber>>();
            sequences.Setup(repository => repository.Table).Returns(new List<SequenceNumber>().AsQueryable());
            donors.Setup(repository => repository.GetById(2)).Returns(new Donor { Id = 2 });
            accounts.Setup(repository => repository.GetById(9)).Returns(new Account { Id = 9, AccountCode = "41001" });
            service = new DonationsService(
                Mock.Of<IMapper>(), NullLogger<DonationsService>.Instance,
                donations.Object, lines.Object, sequences.Object,
                Mock.Of<IRepository<GeneralLedgerSetting>>(), Mock.Of<IRepository<PaymentTerm>>(),
                Mock.Of<IRepository<Bank>>(), donors.Object, accounts.Object);
        }

        [Theory]
        [InlineData("Cash")]
        [InlineData("Cheque")]
        [InlineData("Credit Card")]
        [InlineData("Debit Card")]
        [InlineData("E-Transfer")]
        public void CreateDonation_ValidPaymentType_SavesHeaderWithoutItemLines(string paymentType)
        {
            var input = ValidDonation();
            input.PaymentType = paymentType;
            input.Posted = true;
            var saved = new DonationInvoiceHeader();
            donations.Setup(repository => repository.Insert(It.IsAny<DonationInvoiceHeader>()))
                .Callback<DonationInvoiceHeader>(entity => { entity.Id = 12; saved = entity; });

            var result = service.CreateDonationInvoice(input);

            Assert.True(result.IsSuccess);
            Assert.Equal(125.50m, saved.Amount);
            Assert.Equal(125.50m, saved.ComputeTotalAmount());
            Assert.Equal(paymentType, saved.PaymentType);
            Assert.Equal(2, saved.DonorId);
            Assert.Equal(9, saved.GlAccountId);
            Assert.False(saved.Posted);
            Assert.Empty(saved.DonationInvoiceLines);
            Assert.Equal(12, result.Value.Id);
        }

        [Theory]
        [InlineData("41000")]
        [InlineData("41999")]
        public void CreateDonation_GlCodeAtBoundary_AcceptsAccount(string accountCode)
        {
            accounts.Setup(repository => repository.GetById(9)).Returns(new Account { Id = 9, AccountCode = accountCode });

            Assert.True(service.CreateDonationInvoice(ValidDonation()).IsSuccess);
        }

        [Theory]
        [InlineData("40999")]
        [InlineData("42000")]
        [InlineData("ABC")]
        public void CreateDonation_InvalidGlCode_RejectsSave(string accountCode)
        {
            accounts.Setup(repository => repository.GetById(9)).Returns(new Account { Id = 9, AccountCode = accountCode });

            var result = service.CreateDonationInvoice(ValidDonation());

            Assert.True(result.IsFailure);
            Assert.Contains("41000 and 41999", result.Error.Message);
            donations.Verify(repository => repository.Insert(It.IsAny<DonationInvoiceHeader>()), Times.Never);
        }

        [Fact]
        public void CreateDonation_MissingDonor_RejectsSave()
        {
            var input = ValidDonation();
            input.DonorId = 999;

            var result = service.CreateDonationInvoice(input);

            Assert.True(result.IsFailure);
            Assert.Contains("Donors module", result.Error.Message);
            donations.Verify(repository => repository.Insert(It.IsAny<DonationInvoiceHeader>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1.001)]
        public void CreateDonation_InvalidAmount_RejectsSave(double amount)
        {
            var input = ValidDonation();
            input.Amount = (decimal)amount;

            Assert.True(service.CreateDonationInvoice(input).IsFailure);
            donations.Verify(repository => repository.Insert(It.IsAny<DonationInvoiceHeader>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Other")]
        public void CreateDonation_InvalidPaymentType_RejectsSave(string paymentType)
        {
            var input = ValidDonation();
            input.PaymentType = paymentType;

            Assert.True(service.CreateDonationInvoice(input).IsFailure);
            donations.Verify(repository => repository.Insert(It.IsAny<DonationInvoiceHeader>()), Times.Never);
        }

        [Fact]
        public void UpdateDonation_ExistingRecord_PreservesPostedStateAndHistoricalLines()
        {
            var existing = new DonationInvoiceHeader { Id = 12, Posted = true };
            existing.DonationInvoiceLines.Add(new DonationInvoiceLine { Id = 20, Amount = 10m, Quantity = 2m });
            donations.Setup(repository => repository.GetAllIncluding(It.IsAny<Expression<Func<DonationInvoiceHeader, object>>[]>()))
                .Returns(new[] { existing }.AsQueryable());
            var input = ValidDonation();
            input.Id = 12;
            input.PaymentType = "Cheque";

            var result = service.UpdateDonationInvoice(input);

            Assert.True(result.IsSuccess);
            Assert.Equal(125.50m, existing.Amount);
            Assert.Equal("Cheque", existing.PaymentType);
            Assert.True(existing.Posted);
            Assert.Single(existing.DonationInvoiceLines);
            lines.Verify(repository => repository.Delete(It.IsAny<DonationInvoiceLine>()), Times.Never);
            donations.Verify(repository => repository.Update(existing), Times.Once);
        }

        private static DonationDto ValidDonation()
        {
            return new DonationDto
            {
                DonorId = 2,
                GlAccountId = 9,
                DonationDate = DateTime.Today,
                PaymentType = "Cash",
                Amount = 125.50m
            };
        }
    }
}