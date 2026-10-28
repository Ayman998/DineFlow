using System.Text.RegularExpressions;
using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DineFlow.Test.Api;

public class ArabicErrorsTests
{
    private static readonly ProblemDetailsFactory Factory = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails(o => o.CustomizeProblemDetails = ArabicErrors.LocalizeValidation)
        .AddControllers().Services
        .BuildServiceProvider()
        .GetRequiredService<ProblemDetailsFactory>();

    private static ProblemDetails Handle(Exception exception, string? acceptLanguage)
    {
        var http = new DefaultHttpContext();
        if (acceptLanguage is not null) http.Request.Headers.AcceptLanguage = acceptLanguage;
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception
        };

        new AppExceptionFilter(Factory).OnException(context);

        Assert.True(context.ExceptionHandled);
        return Assert.IsAssignableFrom<ProblemDetails>(Assert.IsType<ObjectResult>(context.Result).Value);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("ar-JO")]
    [InlineData("ar-JO,ar;q=0.9,en;q=0.8")]
    [InlineData("en;q=0.5, ar")]
    public void Known_code_gets_an_arabic_title_when_arabic_is_preferred(string acceptLanguage)
    {
        var details = Handle(AppException.NotFound("qr_invalid", "This QR code isn't active. Please ask a member of staff for help."), acceptLanguage);

        Assert.Equal("رمز QR هذا غير مفعّل. يُرجى طلب المساعدة من أحد الموظفين.", details.Title);
        Assert.Equal("qr_invalid", details.Extensions["code"]);
        Assert.Equal(404, details.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    [InlineData("en-GB,en;q=0.9,ar;q=0.8")]
    [InlineData("fr")]
    [InlineData("not a header ;;; q=")]
    public void English_and_other_requests_keep_the_english_title(string? acceptLanguage)
    {
        var details = Handle(AppException.BadRequest("tip_too_high", "A tip can be at most 20% of the amount you're paying."), acceptLanguage);

        Assert.Equal("A tip can be at most 20% of the amount you're paying.", details.Title);
        Assert.Equal("tip_too_high", details.Extensions["code"]);
    }

    [Fact]
    public void Unknown_code_falls_back_to_english()
    {
        var details = Handle(AppException.Conflict("brand_new_code", "Something new happened."), "ar");

        Assert.Equal("Something new happened.", details.Title);
        Assert.Equal("brand_new_code", details.Extensions["code"]);
    }

    [Fact]
    public void Wrapped_domain_message_is_translated_by_its_text_before_the_code()
    {
        var details = Handle(AppException.BadRequest("invalid_settings", "Tax must be between 0% and 30%."), "ar");

        Assert.Equal("يجب أن تكون الضريبة بين 0% و30%.", details.Title);
    }

    [Fact]
    public void Unrecognised_wrapped_message_falls_back_to_the_code()
    {
        var details = Handle(AppException.BadRequest("invalid_settings", "Some message nobody translated."), "ar");

        Assert.Equal(ArabicErrors.ByCode["invalid_settings"], details.Title);
    }

    [Fact]
    public void Bare_domain_exception_is_translated_too()
    {
        var details = Handle(new DomainException("Price can't be negative."), "ar");

        Assert.Equal("لا يمكن أن يكون السعر سالبًا.", details.Title);
        Assert.Equal("invalid_request", details.Extensions["code"]);
    }

    [Theory]
    [InlineData("invalid_split", "Split between 2 and 20 people.", "20")]
    [InlineData("invalid_menu", "Item name must be 120 characters or fewer.", "اسم الصنف")]
    [InlineData("invalid_transition", "Order #42 is Completed and can't become Preparing.", "#42")]
    [InlineData("invalid_settlement_file", "Line 7: \"abc\" isn't an amount.", "السطر 7")]
    [InlineData("location_exists", "Table 12 already exists at this venue.", "الطاولة")]
    public void Interpolated_messages_keep_their_details(string code, string english, string expectedFragment)
    {
        var arabic = ArabicErrors.Translate(code, english);

        Assert.NotNull(arabic);
        Assert.Contains(expectedFragment, arabic);
        Assert.DoesNotContain(english, arabic);
    }

    [Fact]
    public void Model_validation_title_and_messages_are_arabic_when_preferred()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptLanguage = "ar";
        var state = new ModelStateDictionary();
        state.AddModelError("Name", "The Name field is required.");
        state.AddModelError("Amount", "The field Amount must be between 1 and 100.");

        var details = Factory.CreateValidationProblemDetails(http, state);

        Assert.Equal(ArabicErrors.ValidationTitle, details.Title);
        Assert.Equal(["هذا الحقل مطلوب."], details.Errors["Name"]);
        Assert.Equal(["يجب أن تكون القيمة بين 1 و100."], details.Errors["Amount"]);
    }

    [Fact]
    public void Model_validation_stays_english_for_english_requests()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptLanguage = "en";
        var state = new ModelStateDictionary();
        state.AddModelError("Name", "The Name field is required.");

        var details = Factory.CreateValidationProblemDetails(http, state);

        Assert.Equal("One or more validation errors occurred.", details.Title);
        Assert.Equal(["The Name field is required."], details.Errors["Name"]);
    }

    // ---- Coverage: every code and every fixed domain message in the source has Arabic ----

    private static readonly Regex[] CodePatterns =
    [
        new(@"AppException\.[A-Za-z]+\(\s*""([a-z_]+)"""),
        new(@"new AppException\(\s*\d+\s*,\s*""([a-z_]+)"""),
        new(@"PromoEvaluation\.Rejected\(\s*""([a-z_]+)""")
    ];

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "DineFlow.Domain")) && Directory.Exists(Path.Combine(dir.FullName, "DineFlow.Api")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Couldn't find the DineFlow source folder above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> SourceFiles(params string[] projects) =>
        projects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(SourceRoot(), p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_error_code_in_the_source_has_arabic()
    {
        var codes = SourceFiles("DineFlow.Api", "DineFlow.Application", "DineFlow.Domain", "DineFlow.Infrastructure")
            .SelectMany(f => CodePatterns.SelectMany(p => p.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)))
            .Append("invalid_request") // AppExceptionFilter's code for a bare DomainException
            .ToHashSet();

        Assert.True(codes.Count > 50, $"Only found {codes.Count} codes; is the source scan still pointing at the right folder?");
        var missing = codes.Where(c => !ArabicErrors.ByCode.ContainsKey(c)).Order().ToList();
        Assert.True(missing.Count == 0, "Add Arabic to ArabicErrors.ByCode for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_fixed_domain_message_has_arabic()
    {
        // Interpolated messages are covered by patterns (see Interpolated_messages_keep_their_details), so only
        // calls built from plain literals are checked here, including the "a" ? "b" : "c" form.
        var literal = new Regex(@"""((?:[^""\\]|\\.)*)""");
        var messages = SourceFiles("DineFlow.Domain", "DineFlow.Application")
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("new DomainException("))
            .Select(line => line[line.IndexOf("new DomainException(", StringComparison.Ordinal)..])
            .Where(call => !call.Contains("$\""))
            .SelectMany(call => literal.Matches(call).Select(m => Regex.Unescape(m.Groups[1].Value)))
            .Where(text => text.Length > 0)
            .ToHashSet();

        Assert.True(messages.Count > 30, $"Only found {messages.Count} domain messages; is the source scan still pointing at the right folder?");
        var missing = messages.Where(m => ArabicErrors.Translate("no_such_code", m) is null).Order().ToList();
        Assert.True(missing.Count == 0, "Add Arabic to ArabicErrors.ByMessage for:\n" + string.Join("\n", missing));
    }
}
