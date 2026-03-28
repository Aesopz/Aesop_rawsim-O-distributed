from setuptools import setup, find_packages

setup(
    name="rmfs_gym",
    version="0.1.0",
    packages=find_packages(),
    install_requires=[
        "pettingzoo>=1.24.0",
        "gymnasium>=0.29.0",
        "numpy>=1.24.0",
    ],
    python_requires=">=3.10",
    description="PettingZoo ParallelEnv wrapper for RAWSim-O RMFS simulation",
)
