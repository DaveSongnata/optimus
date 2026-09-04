using Optimus.Core.Voice;
using Xunit;

namespace Optimus.Core.Tests.Voice
{
    public class VoiceCommandMatcherTests
    {
        [Theory]
        [InlineData("otimizar arquivo", VoiceIntent.Optimize)]
        [InlineData("pode otimizar o arquivo pra mim", VoiceIntent.Optimize)]
        [InlineData("Otimização, por favor.", VoiceIntent.Optimize)]
        [InlineData("analisar o documento", VoiceIntent.Analyze)]
        [InlineData("faz uma análise rápida", VoiceIntent.Analyze)]
        [InlineData("auditar cores e fontes", VoiceIntent.Audit)]
        [InlineData("vamos ver as cores do arquivo", VoiceIntent.Audit)]
        [InlineData("mostra as fontes usadas", VoiceIntent.Audit)]
        [InlineData("muda o idioma para inglês", VoiceIntent.SetLanguageEn)]
        [InlineData("troca pra espanhol", VoiceIntent.SetLanguageEs)]
        [InlineData("volta pro português", VoiceIntent.SetLanguagePt)]
        public void Match_recognises_the_intended_command(string transcript, VoiceIntent expected)
        {
            Assert.Equal(expected, VoiceCommandMatcher.Match(transcript));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("bom dia, tudo bem?")]
        public void Match_returns_unknown_for_empty_or_unrelated_speech(string? transcript)
        {
            Assert.Equal(VoiceIntent.Unknown, VoiceCommandMatcher.Match(transcript));
        }

        [Fact]
        public void Match_prefers_language_switch_over_action_keywords_in_the_same_sentence()
        {
            // "auditar" appears in the sentence, but the operator is asking to switch language first.
            Assert.Equal(VoiceIntent.SetLanguageEn,
                VoiceCommandMatcher.Match("troca pra inglês pra eu auditar melhor"));
        }

        [Fact]
        public void Match_is_accent_and_case_insensitive()
        {
            Assert.Equal(VoiceIntent.Optimize, VoiceCommandMatcher.Match("OTIMIZAR"));
            Assert.Equal(VoiceIntent.Optimize, VoiceCommandMatcher.Match("otimizaçãoo"));
        }

        [Fact]
        public void Word_boundary_needle_does_not_match_inside_an_unrelated_word()
        {
            // "cor" is a whole-word needle for Audit; "cortar" must not trigger it.
            Assert.Equal(VoiceIntent.Unknown, VoiceCommandMatcher.Match("cortar o círculo"));
        }
    }
}
