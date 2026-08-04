using Microsoft.EntityFrameworkCore;

namespace Lassie.Data;

public class LassieDbContext(DbContextOptions<LassieDbContext> options) : DbContext(options)
{
}
