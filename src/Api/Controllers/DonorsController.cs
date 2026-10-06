using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonorDto = Dto.Donors.Donor;
using DonorEntity = Core.Domain.Donations.Donor;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/donors")]
    public class DonorsController : BaseController
    {
        private readonly ApiDbContext _context;

        public DonorsController(ApiDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<DonorDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<DonorDto>>> GetDonorsAsync(CancellationToken cancellationToken)
        {
            var donors = await _context.Donors.AsNoTracking().ToListAsync(cancellationToken);
            return Ok(donors.Select(ToDto).OrderBy(donor => donor.Name).ToList());
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(DonorDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DonorDto>> GetDonorAsync(int id, CancellationToken cancellationToken)
        {
            var donor = await _context.Donors.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
            if (donor == null)
                return NotFound();

            return Ok(ToDto(donor));
        }

        [HttpPost]
        [ProducesResponseType(typeof(DonorDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DonorDto>> CreateDonorAsync(DonorDto input, CancellationToken cancellationToken)
        {
            var donor = new DonorEntity();
            ApplyInput(donor, input);
            _context.Donors.Add(donor);
            await _context.SaveChangesAsync(cancellationToken);
            return CreatedAtAction("GetDonor", new { id = donor.Id }, ToDto(donor));
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateDonorAsync(int id, DonorDto input, CancellationToken cancellationToken)
        {
            var donor = await _context.Donors.FindAsync(new object[] { id }, cancellationToken);
            if (donor == null)
                return NotFound();

            ApplyInput(donor, input);
            await _context.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteDonorAsync(int id, CancellationToken cancellationToken)
        {
            var donor = await _context.Donors.FindAsync(new object[] { id }, cancellationToken);
            if (donor == null)
                return NotFound();

            _context.Donors.Remove(donor);
            await _context.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private static void ApplyInput(DonorEntity donor, DonorDto input)
        {
            donor.DonorType = input.DonorType;
            donor.FirstName = input.DonorType == "Individual" ? input.FirstName.Trim() : string.Empty;
            donor.LastName = input.DonorType == "Individual" ? input.LastName.Trim() : string.Empty;
            donor.CompanyName = input.DonorType == "Company" ? input.CompanyName.Trim() : string.Empty;
            donor.Street = input.Street.Trim();
            donor.Province = input.Province;
            donor.PostalCode = input.PostalCode.Trim().ToUpperInvariant();
            donor.Telephone = input.Telephone.Trim();
            donor.Email = input.Email.Trim();
        }

        private static DonorDto ToDto(DonorEntity donor)
        {
            return new DonorDto
            {
                Id = donor.Id,
                DonorType = donor.DonorType,
                FirstName = donor.FirstName,
                LastName = donor.LastName,
                CompanyName = donor.CompanyName,
                Street = donor.Street,
                Province = donor.Province,
                PostalCode = donor.PostalCode,
                Telephone = donor.Telephone,
                Email = donor.Email
            };
        }
    }
}