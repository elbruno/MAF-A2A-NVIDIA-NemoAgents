from __future__ import annotations

from nat.builder.builder import Builder
from nat.builder.framework_enum import LLMFrameworkEnum
from nat.builder.function_info import FunctionInfo
from nat.cli.register_workflow import register_function
from nat.data_models.function import FunctionBaseConfig

from .analysis_tools import analyze_time_series_data
from .analysis_tools import calculate_metrics
from .analysis_tools import detect_data_anomalies
from .analysis_tools import generate_insights


class AnalyzeTimeSeriesConfig(FunctionBaseConfig, name="analyze_time_series"):
    """Register the time-series analysis tool."""


class DetectAnomaliesConfig(FunctionBaseConfig, name="detect_anomalies"):
    """Register the anomaly detection tool."""


class CalculateMetricsConfig(FunctionBaseConfig, name="calculate_metrics"):
    """Register the descriptive metrics tool."""


class GenerateInsightsConfig(FunctionBaseConfig, name="generate_insights"):
    """Register the insight generation tool."""


@register_function(config_type=AnalyzeTimeSeriesConfig, framework_wrappers=[LLMFrameworkEnum.LANGCHAIN])
async def register_analyze_time_series(_config: AnalyzeTimeSeriesConfig, _builder: Builder):
    async def _analyze_time_series(data_points: list[dict[str, object]], metric_name: str = "unknown_metric") -> dict[str, object]:
        """Analyze time-series data to detect direction, spread, and trend strength."""
        return analyze_time_series_data(data_points=data_points, metric_name=metric_name)

    yield FunctionInfo.from_fn(_analyze_time_series, description=_analyze_time_series.__doc__)


@register_function(config_type=DetectAnomaliesConfig, framework_wrappers=[LLMFrameworkEnum.LANGCHAIN])
async def register_detect_anomalies(_config: DetectAnomaliesConfig, _builder: Builder):
    async def _detect_anomalies(
        data_points: list[dict[str, object]],
        sensitivity: float = 2.0,
        metric_name: str = "unknown_metric",
    ) -> dict[str, object]:
        """Detect anomalous values in a time series with a configurable sensitivity threshold."""
        return detect_data_anomalies(data_points=data_points, sensitivity=sensitivity, metric_name=metric_name)

    yield FunctionInfo.from_fn(_detect_anomalies, description=_detect_anomalies.__doc__)


@register_function(config_type=CalculateMetricsConfig, framework_wrappers=[LLMFrameworkEnum.LANGCHAIN])
async def register_calculate_metrics(_config: CalculateMetricsConfig, _builder: Builder):
    async def _calculate_metrics(
        data_points: list[dict[str, object]],
        metric_name: str = "unknown_metric",
        include_percentiles: bool = True,
    ) -> dict[str, object]:
        """Calculate summary statistics and optional percentiles for a time series."""
        return calculate_metrics(
            data_points=data_points,
            metric_name=metric_name,
            include_percentiles=include_percentiles,
        )

    yield FunctionInfo.from_fn(_calculate_metrics, description=_calculate_metrics.__doc__)


@register_function(config_type=GenerateInsightsConfig, framework_wrappers=[LLMFrameworkEnum.LANGCHAIN])
async def register_generate_insights(_config: GenerateInsightsConfig, _builder: Builder):
    async def _generate_insights(
        analysis_results: dict[str, object],
        anomalies: dict[str, object],
        metrics: dict[str, object],
    ) -> dict[str, object]:
        """Turn analysis, anomaly, and metric outputs into business-facing insights and recommendations."""
        return generate_insights(analysis_results=analysis_results, anomalies=anomalies, metrics=metrics)

    yield FunctionInfo.from_fn(_generate_insights, description=_generate_insights.__doc__)
