using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AdminApi.Models_v2_1;
using Microsoft.AspNetCore.Cors;
using AdminApi.Services;
using AdminApi.Helpers;
using AdminApi.Entities;
using AdminApi.Models_v2_1.Validation;
using System;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using AdminApi.Services.Context;

namespace AdminApi.Controllers
{
    /**
     * UsersController
     * This controller handles all routes in the format: "/users/"
     * To disable authentication, simply comment out the [Authorize] annotation
     * 
    **/
    [ApiController]
    [Route("users")]
    public class UsersController : ControllerBase
    {
        private readonly hair_project_dbContext _context;
        private readonly IUsersContext _usersContext;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IEmailService _emailService;

        public UsersController(hair_project_dbContext context,
            IAuthenticationService authenticationService,
            IAuthorizationService authorizationService,
            IEmailService emailService,
            IUsersContext usersContext)
        {
            _context = context;
            _authenticationService = authenticationService;
            _authorizationService = authorizationService;
            _emailService = emailService;
            _usersContext = usersContext;
        }

        // GET: users
        // GET: users?limit=5&offset=0&search=admin (optional pagination / search)
        
        [HttpGet]
        public async Task<ActionResult<List<Users>>> GetUsers(
            [FromQuery(Name = "limit")] string limit = "1000",
            [FromQuery(Name = "offset")] string offset = "0",
            [FromQuery(Name = "search")] string search = ""
            )
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            if (limit != null && offset != null)
            {
                if (!int.TryParse(limit, out int l) || !int.TryParse(offset, out int o))
                {
                    var response = new JsonResponse
                    {
                        Message = "Invalid request",
                        Status = 400,
                        Errors = new Dictionary<string, string[]>
                        {
                            { "Queries", new string[] { "Invalid queries" } }
                        }
                    };

                    return BadRequest(response.FormatResponse());
                }
            }
            
            List<Users> users = await _usersContext.Browse(limit, offset, search);
            return Ok(new { users });
        }

        [HttpGet("count")]
        public async Task<ActionResult<int>> GetUsersCount([FromQuery(Name = "search")] string search = "")
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchUsersCount = await _context.Users.Where(
                    u => 
                    u.UserName.Trim().ToLower().Contains(search.Trim().ToLower()) || 
                    u.UserEmail.Trim().ToLower().Contains(search.Trim().ToLower())
                    )
                    .CountAsync();

                return Ok(new
                {
                    count = searchUsersCount
                });
            }

            var usersCount = await _context.Users.CountAsync();
            return Ok(new
            {
                count = usersCount
            });
        }

        // GET: users/5
        [HttpGet("{id:long}")]
        public async Task<ActionResult<Users>> GetUser(ulong id)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            var users = await _context.Users.Where(u => u.Id == id).Include(u => u.UserFeatures).ToListAsync();

            if (users.Count < 1)
            {
                return NotFound();
            }
            else
            {
                // var mappedUser = await MapFeaturesToUsers(user);
                var userWithoutPassword = users[0].WithoutPassword();
                var userResponse = new
                {
                    user = userWithoutPassword
                };

                return Ok(userResponse);
            }

            // return users;
        }

        // GET: users/{guid} - Can be used to get user details based on their recover password token (if valid)
        [HttpGet("{token:guid}")]
        public async Task<ActionResult<Users>> GetUser(Guid token)
        {
            if (token == null || token == Guid.Empty)
            {
                return BadRequest(new { errors = new { Token = new string[] { "Invalid token" } }, status = 400 });
            }

            var associatedAccount = await _context.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE recover_password_token = UNHEX(REPLACE({token}, {"-"}, {""}))").ToListAsync();

            if (associatedAccount.Count > 0)
            {
                var userId = associatedAccount[0].UserId;
                var associatedUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (associatedUser != null)
                {
                    return Ok(new
                    {
                        associatedUser.UserEmail
                    });
                }

                return NotFound(new { errors = new { Account = new string[] { "No user associated with the token provided was found" } }, status = 404 });
            }

            return NotFound(new { errors = new { Account = new string[] { "No account associated with the token provided was found" } }, status = 404 });
        }


        // GET: /users/logout
        [HttpGet("logout")]
        public IActionResult LogoutUser()
        {
            // Invalidate token/cookie
            Response.Cookies.Append("auth", "", new CookieOptions {
                HttpOnly = true,
                Expires = DateTime.Now.AddDays(-1),
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Domain = Program.API_DOMAIN,
                Secure = true
            });

            return Ok(new { message = "Logout successful" });
        }

        // ********************************************************************************************************************************************        
        // PUT: users/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutUsers(ulong id, [FromBody] UpdatedUser user)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            if (id != user.Id)
            {
                return BadRequest(new { errors = new { Id = new string[] { "ID sent does not match the one in the endpoint" } }, status = 400 });
            }

            var existingUserName = await _context.Users.AnyAsync(u => u.Id != user.Id && u.UserName == user.UserName);

            if (existingUserName)
            {
                return Conflict(new { errors = new { UserName = new string[] { "Username is already taken" } }, status = 409 });
            }

            var existingEmail = await _context.Users.AnyAsync(u => u.Id != user.Id && u.UserEmail == user.UserEmail);

            if (existingEmail)
            {
                return Conflict(new { errors = new { UserEmail = new string[] { "Email is already registered" } }, status = 409 });
            }

            // hash/salt new password
            string salt = _authenticationService.GenerateSalt();
            string hash = _authenticationService.HashPassword(user.UserPassword, salt);

            Users currentUser = await _context.Users.FindAsync(user.Id);

            try
            {
                if (currentUser != null)
                {
                    currentUser.UserName = user.UserName;
                    currentUser.UserPasswordHash = hash;
                    currentUser.UserPasswordSalt = salt;
                    currentUser.FirstName = user.FirstName;
                    currentUser.LastName = user.LastName ?? currentUser?.LastName;
                    currentUser.UserEmail = user.UserEmail;
                    currentUser.UserRole = user.UserRole ?? currentUser?.UserRole;
                    currentUser.DateCreated = currentUser?.DateCreated;
                    _context.Entry(currentUser).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    return Ok();
                }
                return NotFound();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(500);
            }
        }

        // ********************************************************************************************************************************************

        // PUT: users/{guid}/change_password : Method to change user password (based on user's recover password token).
        [HttpPut("{token:guid}/change_password")]
        public async Task<IActionResult> SetNewPassword(Guid token, [FromBody] ValidatedChangeUserPasswordModel user)
        {
            var existingToken = await _context.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE recover_password_token = UNHEX(REPLACE({token}, {"-"}, {""}))").ToListAsync();

            if (existingToken.Count > 0)
            {
                // token was found, get user id and change their password
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == existingToken[0].UserId);

                if (existingUser != null)
                {
                    string salt = _authenticationService.GenerateSalt();
                    string hash = _authenticationService.HashPassword(user.UserPassword, salt);

                    existingUser.UserPasswordHash = hash;
                    existingUser.UserPasswordSalt = salt;

                    _context.Entry(existingUser).State = EntityState.Modified;

                    // invalidate token, now that the password has changed
                    var accountEntry = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == existingUser.Id);
                    if (accountEntry != null)
                    {
                        accountEntry.RecoverPasswordToken = null;
                        _context.Entry(accountEntry).State = EntityState.Modified;
                    }

                    await _context.SaveChangesAsync();

                    var origin = Request.Headers["Origin"];
                    var forgotPasswordLink = $@"{origin}/forgot_password";

                    var emailBody = $@"Hi {existingUser.UserName},

 Your password has been reset @HairdressingProject Admin Portal. If you have not made this request, please contact us or navigate to the page below to reset it again:

 {forgotPasswordLink}

 Regards,

 HairdressingProject Admin.
 ";
                    try
                    {
                        _emailService.SendEmail(existingUser.UserEmail, existingUser.UserName, "Password successfully reset", emailBody);
                        return NoContent();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to send email:");
                        Console.WriteLine(ex);

                        return StatusCode(StatusCodes.Status500InternalServerError, new { errors = new { Email = new string[] { ex.Message } } });
                    }
                }
                return NotFound(new { errors = new { Token = new string[] { "User not found" } }, status = 404 });
            }
            return NotFound(new { errors = new { Token = new string[] { "Token not found" } }, status = 404 });
        }

        // PUT: users/5/change_password : Method to change user password (based on user's ID).
        [HttpPut("{id:long}/change_password")]
        public async Task<IActionResult> SetNewPassword(ulong id, [FromBody]ValidatedChangeUserPasswordModel user)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            if (id != user.Id)
            {
                return BadRequest(new { errors = new { Id = new string[] { "ID sent does not match the one in the endpoint" } }, status = 400 });
            }

            var userMod = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (userMod == null)
            {
                return NotFound(new { errors = new { Id = new string[] { "User not found" } }, status = 404 });
            }

            // hash + salt new password
            string salt = _authenticationService.GenerateSalt();
            string hash = _authenticationService.HashPassword(user.UserPassword, salt);

            userMod.UserPasswordHash = hash;
            userMod.UserPasswordSalt = salt;

            _context.Entry(userMod).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UsersExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // PUT users/5/change_role
        [HttpPut("{id}/change_role")]
        public async Task<IActionResult> ChangeUserRole(ulong id, [FromBody] Models_v2_1.Validation.ValidatedUserRoleModel user)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            if (id != user.Id)
            {
                return BadRequest(new { errors = new { Id = new string[] { "ID sent does not match the one in the endpoint" } }, status = 400 });
            }

            var userMod = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.UserName == user.UserName && u.UserEmail == user.UserEmail);

            if (userMod == null)
            {
                return NotFound(new { errors = new { Id = new string[] { "User not found" } }, status = 404 });
            }

            userMod.UserRole = user.UserRole;

            _context.Entry(userMod).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UsersExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // ********************************************************************************************************************************************        
        // POST users
        
        [HttpPost]
        public async Task<ActionResult<Users>> PostUsers([FromBody] SignUpUser user)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            var existingUser = await _context.Users.AnyAsync(u => u.UserName == user.UserName || u.UserEmail == user.UserEmail);

            if (!existingUser)
            {
                string salt = _authenticationService.GenerateSalt();
                string hash = _authenticationService.HashPassword(user.UserPassword, salt);

                Users userToBeAdded = new Users
                {
                    UserName = user.UserName,
                    UserEmail = user.UserEmail,
                    FirstName = user.FirstName,
                    LastName = user.LastName ?? "",
                    UserRole = user.UserRole,
                    UserPasswordHash = hash,
                    UserPasswordSalt = salt
                };

                _context.Users.Add(userToBeAdded);

                await _context.SaveChangesAsync();

                var addedUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == userToBeAdded.UserName || u.UserEmail == userToBeAdded.UserEmail);

                return CreatedAtAction("GetUsers", new { id = addedUser.Id }, userToBeAdded.WithoutPassword());
            }

            // return Conflict(new { errors =  "User already exists" });
            return Conflict(new { errors = new { Users = new string[] { "User already exists" } }, status = 409 });
        }

        // POST users/sign_up
        
        [HttpPost("sign_up")]
        public async Task<IActionResult> SignUp([FromBody] SignUpUser newUser)
        {
            if (string.IsNullOrWhiteSpace(Request.Headers["Origin"]))
            {
                return Unauthorized(new { errors = new { Origin = new string[] { "Invalid request origin" } }, status = 401 });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName.Trim().ToLower() == newUser.UserName.Trim().ToLower() || u.UserEmail.Trim().ToLower() == newUser.UserEmail.Trim().ToLower());

            if (user == null)
            {
                // New user, add to DB and authenticate
                // Also, validate/sanitise properties here

                string salt = _authenticationService.GenerateSalt();
                string hash = _authenticationService.HashPassword(newUser.UserPassword, salt);

                // By default, every new user will be registered as "user" in their user role
                // Their status should only be changed by admins
                Users userToBeAdded = new Users
                {
                    UserName = newUser.UserName,
                    UserEmail = newUser.UserEmail,
                    FirstName = newUser.FirstName,
                    LastName = newUser.LastName ?? "",
                    UserPasswordHash = hash,
                    UserPasswordSalt = salt
                };

                _context.Users.Add(userToBeAdded);

                await _context.SaveChangesAsync();

                // Get newly created user from database to create a new account record
                // Stored procedures would be preferred in this case in order to avoid making so many calls to the database
                var savedUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == newUser.UserName);

                // TODO: Handle the opposite case
                if (savedUser != null)
                {
                    _context.Accounts.Add(new Accounts { UserId = savedUser.Id });
                    await _context.SaveChangesAsync();
                }

                var authenticatedUser = await _authenticationService.Authenticate(newUser.UserName, newUser.UserPassword);
                // var baseUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == authenticatedUser.Id);

                authenticatedUser.BaseUser = savedUser.WithoutPassword();

                // Send cookie with fresh token
                _authorizationService.SetAuthCookie(Request, Response, authenticatedUser.Token);

                authenticatedUser.Token = null;

                // Send back newly created user without token
                return CreatedAtAction(nameof(GetUser), new { authenticatedUser.Id }, authenticatedUser);
            }

            // Existing user, return 409 (Conflict)
            // Alternatively, refresh this user's token
            return Conflict(new { errors = new { Users = new string[] { "User already registered" } }, status = 409 });
        }

        // POST: users/sign_in
        // 
        [HttpPost("sign_in")]
        public async Task<IActionResult> SignIn([FromBody] AuthenticatedUserModel user)
        {
            if (string.IsNullOrWhiteSpace(Request.Headers["Origin"]))
            {
                var response = new JsonResponse
                {
                    Message = "Invalid request",
                    Status = 401,
                    Errors = new Dictionary<string, string[]>
                    {
                        { "Origin", new string[] {"Invalid request origin"} }
                    }
                };

                return Unauthorized(response.FormatResponse());
            }

            // Authenticate user
            var authenticatedUser = await _authenticationService.Authenticate(user.UserNameOrEmail, user.UserPassword);

            if (authenticatedUser == null)
            {
                // User isn't registered
                Response.Headers.Append("Access-Control-Allow-Origin", Request.Headers["Origin"]);
                return Unauthorized(new { errors = new { Authentication = new string[] { "Invalid username, email and/or password" } }, status = 401 });
            }

            // Return 200 OK with token in cookie
            var existingUser = await _context.Users.Where(u => u.Id == authenticatedUser.Id).Include(u => u.UserFeatures).FirstOrDefaultAsync();

            authenticatedUser.BaseUser = existingUser;

            _authorizationService.SetAuthCookie(Request, Response, authenticatedUser.Token);

            return Ok();
        }

        // POST: users/authenticate
        // This method is an alternative to sign in that validates the token directly
        
        [HttpGet("authenticate")]
        public async Task<IActionResult> AuthenticateUser()
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            var id = _authenticationService.GetUserIdFromToken(Request.Cookies["auth"]);

            if (id != null && ulong.TryParse(id, out ulong _id))
            {
                var user = await _context.Users.FindAsync(_id);
                return Ok(new { Id = user.Id, UserRole = user.UserRole });
            }
            return NotFound();
        }

        // POST: users/forgot_password
        
        [HttpPost("forgot_password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ValidatedUserEmailModel user)
        {
            var origin = Request.Headers["Origin"];

            if (string.IsNullOrEmpty(origin))
            {
                return BadRequest(new { errors = new { Origin = new string[] { "Request origin was not supplied" } }, status = 400 });
            }

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == user.UserNameOrEmail || u.UserName == user.UserNameOrEmail);

            if (existingUser == null)
            {
                return NotFound(new { errors = new { UserNameOrEmail = new string[] { "Username/email is not registered" } }, status = 404 });
            }

            var existingAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == existingUser.Id);

            if (existingAccount == null)
            {
                return NotFound(new { errors = new { Account = new string[] { "Unable to retrieve account details" } }, status = 404 });
            }

            var recoverPasswordToken = Guid.NewGuid();

            await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE accounts SET recover_password_token = UNHEX(REPLACE({recoverPasswordToken}, {"-"}, {""})) WHERE user_id = {existingAccount.UserId}");

            await _context.SaveChangesAsync();

            var recoverPasswordLink = $@"{origin}/reset_password?token={recoverPasswordToken}";

            var emailBody = $@"Hi {existingUser.UserName},

It seems that you have requested to recover your password @HairdressingProject Admin Portal. If you have not, please ignore this email.

Use this link to do so: {recoverPasswordLink}

Regards,

HairdressingProject Admin.
";
            try
            {
                _emailService.SendEmail(existingUser.UserEmail, existingUser.UserName, "Recover Password", emailBody);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to send email:");
                Console.WriteLine(ex);

                return StatusCode(StatusCodes.Status500InternalServerError, new { errors = new { Email = new string[] { ex.Message } } });
            }
        }

        // DELETE: users/5
        [HttpDelete("{id}")]
        public async Task<ActionResult<Users>> DeleteUsers(ulong id)
        {
            if (!_authorizationService.ValidateJWTCookie(Request))
            {
                return Unauthorized(new { errors = new { Token = new string[] { "Invalid token" } }, status = 401 });
            }

            var users = await _context.Users.FindAsync(id);
            if (users == null)
            {
                return NotFound();
            }

            _context.Users.Remove(users);
            await _context.SaveChangesAsync();

            return users;
        }

        private bool UsersExists(ulong id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}
