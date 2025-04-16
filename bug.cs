using NUnit.Framework; // 假设Should()方法来自NUnit框架

class AntisamyTestHelper
{
    // 假设Antisamy和相关类型已经正确引入和定义
    // 这里的Antisamy和Policy是占位，需要根据实际情况替换
    private object antisamy;
    private object policy;

    public void TestSmuggledTagsInStyleContent()
    {
        string result1 = ((dynamic)antisamy).Scan("<style/>b<![CDATA[</style><a href=javascript:alert(1)>test", policy).GetCleanHtml();
        Assert.That(result1, Does.Not.Contain("javascript"));

        string result2 = ((dynamic)antisamy).Scan("<select<style/>W<xmp<script>alert(1)</script>", policy).GetCleanHtml();
        Assert.That(result2, Does.Not.Contain("script"));

        string result3 = ((dynamic)antisamy).Scan("<select<style/>k<input<</>input/onfocus=alert(1)>", policy).GetCleanHtml();
        Assert.That(result3, Does.Not.Contain("input"));

        string result4 = ((dynamic)antisamy).Scan("<style/><listing/>]]><noembed></style><img src=x onerror=mxss(1)></noembed>", policy).GetCleanHtml();
        Assert.That(result4, Does.Not.Contain("mxss"));

        string result5 = ((dynamic)antisamy).Scan("<style/><math>'<noframes ></style><img src=x onerror=mxss(1)></noframes>'", policy).GetCleanHtml();
        Assert.That(result5, Does.Not.Contain("mxss"));
    }
}
