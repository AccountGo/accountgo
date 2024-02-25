namespace Dto.Inventory.Response
{
    public class GetMeasurement : BaseDto
    {
        public string Code { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return Description;
        }
    }
}
